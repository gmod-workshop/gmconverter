using System.Numerics;
using System.Text;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
using GMConverter.SDK.Textures;
using GMConverter.SourceEngine.Common;
using GMConverter.SourceEngine.Geometry;

namespace GMConverter.SourceEngine.Exporters;

/// <summary>
/// Exports a model to the Source Engine MDL format.
/// </summary>
internal sealed class MDLExporter : IExporter
{
    private static readonly UTF8Encoding _utf8NoBom = new(false);
    private const int _sourceMaxConvexPieces = 1024;

    private readonly ITextureFactory _textureFactory;

    public MDLExporter(ITextureFactory textureFactory)
    {
        _textureFactory = textureFactory;
    }

    // Source's "1 unit" = 1 inch. Unreal imports normalize centimeters to meters at the import
    // boundary. Multiply by 39.3700787 (in/m) when writing SMD
    // so the exported MDL renders at its real-world size in-engine.
    private const float _metersToSourceUnits = 39.3700787f;
    private const double _gimbalLockThreshold = 0.001;

    public string OutputFormat => "mdl";

    public string OutputName => "Source Engine";

    // Full schema declaration — three groups (Tools, Materials, Physics). The host's generic
    // options panel renders these directly; CLI registers a --mdl-<key> argument per descriptor.
    public OptionSchema OptionSchema { get; } = new(
    [
        new OptionGroup("tools", "Tools",
        [
            new OptionDescriptor("modelPath", OptionType.String, "Model path")
            {
                Description = "Output MDL path under the game models directory. Defaults to gmconverter/<name>.mdl.",
            },
            new OptionDescriptor("studioMdlPath", OptionType.Path, "StudioMDL override")
            {
                Description = "Optional StudioMDL-CE executable. Auto-downloads to tools/ when omitted.",
            },
            new OptionDescriptor("vtfCmdPath", OptionType.Path, "VTFCmd override")
            {
                Description = "Optional VTFCmd executable. Auto-downloads VTFEdit Reloaded to tools/ when omitted and materials are built.",
            },
            new OptionDescriptor("buildMaterials", OptionType.Bool, "Build materials")
            {
                DefaultValue = true,
                Description = "Compile VTFs and VMTs alongside the MDL. Disable for mesh-only output.",
            },
        ]),
        new OptionGroup("material", "Materials",
        [
            new OptionDescriptor("material:maxTextureSize", OptionType.Enum, "Max texture size")
            {
                DefaultValue = "0",
                Choices = ["0", "512", "1024", "2048", "4096"],
                Description = "Cap the longest edge before VTF compile. 0 disables resizing.",
            },
            new OptionDescriptor("material:deduplicateTextures", OptionType.Bool, "Deduplicate identical textures")
            {
                DefaultValue = false,
                Description = "Hash resized texture content and reuse an existing VTF when materials produce byte-identical maps.",
            },
        ]),
        new OptionGroup("physics", "Physics",
        [
            new OptionDescriptor("physics:enabled", OptionType.Bool, "Generate physics")
            {
                DefaultValue = false,
                Description = "Generate a simple collision mesh alongside the MDL.",
            },
            new OptionDescriptor("physics:mode", OptionType.Enum, "Physics mode")
            {
                DefaultValue = "bounds",
                Choices = ["bounds", "coacd"],
                Description = "bounds: single AABB hull. coacd: native convex decomposition.",
            },
            new OptionDescriptor("physics:mass", OptionType.Float, "Mass (kg)")
            {
                Minimum = 0.1m,
                Maximum = 100000m,
                Increment = 10m,
                DefaultValue = 100f,
            },
            new OptionDescriptor("physics:coacdThreshold", OptionType.Float, "CoACD threshold")
            {
                Minimum = 0.0001m,
                Maximum = 1m,
                Increment = 0.001m,
                DefaultValue = 0.05f,
                Description = "CoACD termination threshold from 0.01 to 1.",
            },
            new OptionDescriptor("physics:maxConvexPieces", OptionType.Int, "Max convex pieces")
            {
                Minimum = 1m,
                Maximum = 128m,
                DefaultValue = 32,
            },
            new OptionDescriptor("physics:maxHullVertices", OptionType.Int, "Max hull vertices")
            {
                Minimum = 4m,
                Maximum = 256m,
                DefaultValue = 32,
            },
        ]),
    ]);

    public void Export(
        Model model,
        string outputDirectory,
        string baseName,
        OptionValues exportOptions)
    {
        var options = BuildOptions(baseName, exportOptions);
        var sourceTools = SourceToolPaths.Resolve(options.StudioMdlPath, options.VtfCmdPath, options.BuildMaterials);
        var physicsOptions = options.Physics;
        var modelPath = options.ModelPath;
        var safeBaseName = NameHelpers.SanitizeFileName(baseName);
        var smdPath = Path.Combine(outputDirectory, $"{safeBaseName}.smd");
        var physicsSmdPath =
            physicsOptions is null ? null : Path.Combine(outputDirectory, $"{safeBaseName}_phys.smd");
        var animationSmdPaths = GetAnimationSmdPaths(model, outputDirectory, safeBaseName);
        var qcPath = Path.Combine(outputDirectory, $"{safeBaseName}.qc");
        var materialRoot = Path.Combine(outputDirectory, "materials");
        var materialRelativeDirectories = GetMaterialDirectories(model, modelPath);
        var materialRelativeDirectory = materialRelativeDirectories[0];
        var materialDirectory =
            Path.Combine(materialRoot, materialRelativeDirectory.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(materialDirectory);

        WriteSmd(model, smdPath);
        if (physicsSmdPath is not null)
        {
            WritePhysicsSmd(model, physicsSmdPath, physicsOptions!);
        }

        foreach (var (clip, animationSmdPath) in animationSmdPaths)
        {
            WriteAnimationSmd(model, clip, animationSmdPath);
        }

        WriteQc(qcPath, model, modelPath, safeBaseName, materialRelativeDirectories, physicsSmdPath, physicsOptions, animationSmdPaths);

        // PNG+VMT fallback path: only useful when VtfCmd isn't going to run. When the compiler is
        // available it owns both files in materialDirectory — and the resize/dedup logic there
        // needs to choose VMT texture references, which this fallback can't see.
        var willCompileMaterials = options.BuildMaterials && sourceTools.CanCompileMaterials;
        if (!willCompileMaterials)
        {
            ExportSourceMaterials(model, materialDirectory, materialRelativeDirectory);
        }

        var result = new MDLExportResult(qcPath, smdPath, physicsSmdPath, materialDirectory, materialRelativeDirectory);
        Compile(model, result, sourceTools, options.BuildMaterials, options.MaterialOptimization);
    }

    // Binds the host's option bag into the strongly-typed MDLExportOptions the rest of this
    // file already knows how to work with. Keeps the option-bag boundary tight to this method.
    private static MDLExportOptions BuildOptions(string baseName, OptionValues o)
    {
        var modelPath = o.GetString("modelPath");
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            modelPath = $"gmconverter/{NameHelpers.SanitizeMaterialName(baseName)}.mdl";
        }

        var physics = o.GetBool("physics:enabled")
            ? new PhysicsOptions(
                Mode: o.GetString("physics:mode") switch
                {
                    "coacd" => PhysicsMode.Coacd,
                    _ => PhysicsMode.Bounds,
                },
                Mass: o.GetFloat("physics:mass", 100f),
                Coacd: o.GetString("physics:mode") == "coacd"
                    ? new CoacdOptions(
                        Threshold: o.GetFloat("physics:coacdThreshold", 0.05f),
                        MaxConvexPieces: o.GetInt("physics:maxConvexPieces", 32),
                        MaxHullVertices: o.GetInt("physics:maxHullVertices", 32))
                    : null)
            : null;

        var material = new MaterialOptimizationOptions(
            MaxTextureSize: o.GetInt("material:maxTextureSize"),
            DeduplicateTextures: o.GetBool("material:deduplicateTextures"));

        return new MDLExportOptions(
            ModelPath: modelPath,
            StudioMdlPath: o.GetString("studioMdlPath"),
            VtfCmdPath: o.GetString("vtfCmdPath"),
            BuildMaterials: o.GetBool("buildMaterials", defaultValue: true),
            Physics: physics,
            MaterialOptimization: material);
    }

    private void Compile(
        Model model,
        MDLExportResult result,
        SourceToolPaths sourceTools,
        bool buildMaterials,
        MaterialOptimizationOptions? materialOptimization)
    {
        if (buildMaterials)
        {
            if (sourceTools.CanCompileMaterials)
            {
                var materialCompiler = new SourceMaterialCompiler(
                    sourceTools.VtfCmdPath!,
                    _textureFactory,
                    materialOptimization ?? MaterialOptimizationOptions.Default);
                materialCompiler.Compile(AllMaterials(model), result.MaterialDirectory, result.MaterialRelativeDirectory);
            }
        }

        RunStudioMdl(sourceTools.StudioMdlPath, result.QcPath);
    }

    private static void RunStudioMdl(string studioMdlPath, string qcPath)
    {
        if (!File.Exists(studioMdlPath))
        {
            throw new GMConverterException($"studiomdl not found: {studioMdlPath}");
        }

        ProcessRunner.Run(studioMdlPath, [qcPath]);
    }

    private static void WriteSmd(Model model, string smdPath)
    {
        using var writer = new StreamWriter(smdPath, false, _utf8NoBom);
        writer.WriteLine("version 1");
        WriteSmdNodes(writer, model.Skeleton);
        WriteReferenceSkeleton(writer, model.Skeleton);
        writer.WriteLine("triangles");

        // Source's VMT has no equivalent of glTF's KHR_texture_transform, so when the MultiLayerBaker
        // produced a tile-extended texture for multi-layer parts the wedge UVs that go beyond [0,1]
        // sample off the end of the baked PNG in Source. We bake the per-material BakedUv0Scale
        // into the SMD wedge UVs here so each submesh's UVs map directly into [0,1] of its texture.
        var uvScales = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        foreach (var material in model.Materials.Where(m => m.BakedUv0Scale is not null))
        {
            uvScales[material.Name] = material.BakedUv0Scale!.Value;
        }

        foreach (var mesh in model.Meshes)
        {
            foreach (var submesh in mesh.Submeshes)
            {
                var materialName = submesh.MaterialName ?? "default";
                var uvScale = uvScales.GetValueOrDefault(materialName, Vector2.One);

                foreach (var triangle in submesh.Triangles)
                {
                    writer.WriteLine(materialName);
                    WriteSmdVertex(writer, mesh.Vertices[triangle.A], uvScale);
                    WriteSmdVertex(writer, mesh.Vertices[triangle.C], uvScale);
                    WriteSmdVertex(writer, mesh.Vertices[triangle.B], uvScale);
                }
            }
        }

        writer.WriteLine("end");
    }

    private static void WriteSmdVertex(StreamWriter writer, Vertex vertex, Vector2 uvScale)
    {
        var position = vertex.Position * _metersToSourceUnits;
        var normal = vertex.Normal;
        var uv = vertex.TextureCoordinate * uvScale;
        var weights = NormalizeSmdWeights(vertex.BoneWeights);
        var parentBoneIndex = weights.Length == 0 ? 0 : weights[0].BoneIndex;
        var weightText = weights.Length == 0
            ? string.Empty
            : " " + weights.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
              string.Concat(weights.Select(weight => FormattableString.Invariant($" {weight.BoneIndex} {weight.Weight:0.######}")));

        writer.WriteLine(FormattableString.Invariant(
            $"{parentBoneIndex} {position.X:0.######} {position.Y:0.######} {position.Z:0.######} {normal.X:0.######} {normal.Y:0.######} {normal.Z:0.######} {uv.X:0.######} {uv.Y:0.######}{weightText}"));
    }

    private static VertexBoneWeight[] NormalizeSmdWeights(IReadOnlyList<VertexBoneWeight>? weights)
    {
        var validWeights = weights?
            .Where(weight => weight.BoneIndex >= 0 && weight.Weight > 0.0f)
            .GroupBy(weight => weight.BoneIndex)
            .Select(group => new VertexBoneWeight(group.Key, group.Sum(weight => weight.Weight)))
            .OrderByDescending(weight => weight.Weight)
            .ToArray();

        if (validWeights is not { Length: > 0 })
        {
            return [];
        }

        var totalWeight = validWeights.Sum(weight => weight.Weight);
        if (totalWeight <= 0.000001f)
        {
            return [];
        }

        return [.. validWeights.Select(weight => new VertexBoneWeight(weight.BoneIndex, weight.Weight / totalWeight))];
    }

    private static void WritePhysicsSmd(Model model, string physicsSmdPath, PhysicsOptions physicsOptions)
    {
        switch (physicsOptions.Mode)
        {
            case PhysicsMode.Bounds:
                WriteBoundsPhysicsSmd(model, physicsSmdPath);
                break;

            case PhysicsMode.Coacd:
                WriteCoacdPhysicsSmd(model, physicsSmdPath,
                    physicsOptions.Coacd ?? throw new GMConverterException("Missing CoACD options."));
                break;

            default:
                throw new GMConverterException($"Unsupported physics mode: {physicsOptions.Mode}");
        }
    }

    private static void WriteBoundsPhysicsSmd(Model model, string physicsSmdPath)
    {
        var bounds = model.Bounds().WithMinimumThickness(1f / _metersToSourceUnits);
        using var writer = CreatePhysicsSmdWriter(physicsSmdPath);

        Vector3[] vertices =
        [
            new(bounds.Min.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Max.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Max.Z)
        ];

        WritePhysicsQuad(writer, vertices, 0, 3, 2, 1, -Vector3.UnitZ);
        WritePhysicsQuad(writer, vertices, 4, 5, 6, 7, Vector3.UnitZ);
        WritePhysicsQuad(writer, vertices, 0, 1, 5, 4, -Vector3.UnitY);
        WritePhysicsQuad(writer, vertices, 3, 7, 6, 2, Vector3.UnitY);
        WritePhysicsQuad(writer, vertices, 0, 4, 7, 3, -Vector3.UnitX);
        WritePhysicsQuad(writer, vertices, 1, 2, 6, 5, Vector3.UnitX);

        writer.WriteLine("end");
    }

    private static void WriteCoacdPhysicsSmd(Model model, string physicsSmdPath, CoacdOptions options)
    {
        var parts = CoacdNative.Decompose(
            model.Merge(),
            new CoacdDecompositionOptions(options.Threshold, options.MaxConvexPieces, options.MaxHullVertices));

        if (parts.Count == 0)
        {
            throw new GMConverterException("CoACD did not produce any convex parts.");
        }

        WritePhysicsPartsSmd(physicsSmdPath, parts);
    }

    private static void WritePhysicsPartsSmd(string physicsSmdPath, IReadOnlyList<Mesh> parts)
    {
        using var writer = CreatePhysicsSmdWriter(physicsSmdPath);
        var triangleCount = 0;

        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var materialName = FormattableString.Invariant($"physics_{partIndex}");
            var partNormal = GetPartNormal(part);

            foreach (var triangle in part.Triangles)
            {
                WritePhysicsTriangle(
                    writer,
                    part.Vertices[triangle.A].Position,
                    part.Vertices[triangle.B].Position,
                    part.Vertices[triangle.C].Position,
                    partNormal,
                    materialName);
                triangleCount++;
            }
        }

        if (triangleCount == 0)
        {
            throw new GMConverterException("CoACD produced convex parts, but none of them contained triangles.");
        }

        writer.WriteLine("end");
    }

    private static StreamWriter CreatePhysicsSmdWriter(string physicsSmdPath)
    {
        var writer = new StreamWriter(physicsSmdPath, false, _utf8NoBom);
        writer.WriteLine("version 1");
        WriteSmdNodes(writer, null);
        WriteReferenceSkeleton(writer, null);
        writer.WriteLine("triangles");
        return writer;
    }

    private static Vector3 GetPartNormal(Mesh part)
    {
        foreach (var triangle in part.Triangles)
        {
            var normal = triangle.Normal(part.Vertices);

            if (normal != Vector3.UnitZ)
            {
                return normal;
            }
        }

        return Vector3.UnitZ;
    }

    private static void WritePhysicsQuad(StreamWriter writer, Vector3[] vertices, int a, int b, int c,
        int d, Vector3 normal)
    {
        WritePhysicsTriangle(writer, vertices[a], vertices[b], vertices[c], normal);
        WritePhysicsTriangle(writer, vertices[a], vertices[c], vertices[d], normal);
    }

    private static void WritePhysicsTriangle(StreamWriter writer, Vector3 a, Vector3 b, Vector3 c,
        Vector3 normal)
    {
        WritePhysicsTriangle(writer, a, b, c, normal, "physics");
    }

    private static void WritePhysicsTriangle(StreamWriter writer, Vector3 a, Vector3 b, Vector3 c,
        Vector3 normal, string materialName)
    {
        writer.WriteLine(materialName);
        WritePhysicsVertex(writer, a, normal);
        WritePhysicsVertex(writer, b, normal);
        WritePhysicsVertex(writer, c, normal);
    }

    private static void WritePhysicsVertex(StreamWriter writer, Vector3 vertex, Vector3 normal)
    {
        var scaled = vertex * _metersToSourceUnits;
        writer.WriteLine(FormattableString.Invariant(
            $"0 {scaled.X:0.######} {scaled.Y:0.######} {scaled.Z:0.######} {normal.X:0.######} {normal.Y:0.######} {normal.Z:0.######} 0 0"));
    }

    private static void WriteQc(
        string qcPath,
        Model model,
        string modelPath,
        string safeBaseName,
        IReadOnlyList<string> materialRelativeDirectories,
        string? physicsSmdPath,
        PhysicsOptions? physicsOptions,
        (AnimationClip Clip, string SmdPath)[] animationSmdPaths)
    {
        var smdFileName = $"{safeBaseName}.smd";
        using var writer = new StreamWriter(qcPath, false, _utf8NoBom);

        writer.WriteLine(FormattableString.Invariant($"$modelname \"{modelPath.Replace('\\', '/')}\""));
        writer.WriteLine(FormattableString.Invariant($"$body \"body\" \"{smdFileName}\""));
        foreach (var materialRelativeDirectory in materialRelativeDirectories)
        {
            writer.WriteLine(FormattableString.Invariant($"$cdmaterials \"{materialRelativeDirectory.Replace('\\', '/')}\""));
        }

        if (model.Skeleton is null && animationSmdPaths.Length == 0)
        {
            writer.WriteLine("$staticprop");
        }

        writer.WriteLine("$surfaceprop \"metal\"");
        WriteTextureGroup(writer, model);

        writer.WriteLine("$sequence \"idle\" \"{0}\" fps 1", smdFileName);

        if (animationSmdPaths.Length > 0)
        {
            foreach (var (clip, animationSmdPath) in animationSmdPaths)
            {
                writer.WriteLine(FormattableString.Invariant(
                    $"$sequence \"{EscapeQcString(clip.Name)}\" \"{Path.GetFileName(animationSmdPath)}\" fps {clip.FrameRate:0.######}"));
            }
        }

        if (physicsSmdPath is not null)
        {
            writer.WriteLine(FormattableString.Invariant($"$collisionmodel \"{Path.GetFileName(physicsSmdPath)}\""));
            writer.WriteLine("{");
            if (physicsOptions?.Mode is PhysicsMode.Coacd)
            {
                writer.WriteLine("    $concave");
                writer.WriteLine(FormattableString.Invariant($"    $maxconvexpieces {_sourceMaxConvexPieces}"));
            }

            writer.WriteLine(FormattableString.Invariant($"    $mass {(physicsOptions?.Mass ?? 100.0f):0.###}"));
            writer.WriteLine("}");
        }
    }

    // One skin family per MaterialSkin after the default. Only materials some skin replaces get a
    // column; studiomdl leaves the rest unchanged in every family.
    private static void WriteTextureGroup(StreamWriter writer, Model model)
    {
        if (model.Skins is not { Count: > 0 } skins)
        {
            return;
        }

        var usedMaterials = model.Meshes
            .SelectMany(mesh => mesh.Submeshes)
            .Select(submesh => submesh.MaterialName ?? "default")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columns = skins
            .SelectMany(skin => skin.Replacements.Keys)
            .Where(usedMaterials.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (columns.Length == 0)
        {
            return;
        }

        writer.WriteLine("$texturegroup \"skinfamilies\"");
        writer.WriteLine("{");
        WriteSkinFamily(writer, columns);
        foreach (var skin in skins)
        {
            WriteSkinFamily(writer, columns.Select(name => skin.Replacements.TryGetValue(name, out var replacement) ? replacement.Name : name));
        }

        writer.WriteLine("}");
    }

    private static void WriteSkinFamily(StreamWriter writer, IEnumerable<string> materialNames)
    {
        writer.WriteLine("    { " + string.Join(' ', materialNames.Select(name => $"\"{EscapeQcString(name)}\"")) + " }");
    }

    private static (AnimationClip Clip, string SmdPath)[] GetAnimationSmdPaths(
        Model model,
        string outputDirectory,
        string safeBaseName)
    {
        if (model.Skeleton is null || model.Animations is not { Count: > 0 })
        {
            return [];
        }

        return
        [
            .. model.Animations
            .Where(clip => clip.Tracks.OfType<BoneTransformTrack>().Any())
            .Select((clip, index) => (
                clip,
                Path.Combine(outputDirectory, $"{safeBaseName}_{index}_{NameHelpers.SanitizeFileName(clip.Name)}.smd")))
        ];
    }

    private static void WriteAnimationSmd(Model model, AnimationClip clip, string smdPath)
    {
        if (model.Skeleton is null)
        {
            return;
        }

        using var writer = new StreamWriter(smdPath, false, _utf8NoBom);
        writer.WriteLine("version 1");
        WriteSmdNodes(writer, model.Skeleton);
        writer.WriteLine("skeleton");

        var tracksByBone = clip.Tracks
            .OfType<BoneTransformTrack>()
            .GroupBy(track => track.BoneIndex)
            .ToDictionary(group => group.Key, group => group.First());
        var lastFrame = GetLastAnimationFrame(clip, tracksByBone.Values);

        for (var frameIndex = 0; frameIndex <= lastFrame; frameIndex++)
        {
            writer.WriteLine(FormattableString.Invariant($"time {frameIndex}"));
            var frameTransforms = model.Skeleton.Bones.ToDictionary(
                bone => bone.Index,
                bone => GetFrameTransform(bone, tracksByBone.GetValueOrDefault(bone.Index), frameIndex, clip.FrameRate));
            var sourceTransforms = BuildSourceLocalTransforms(model.Skeleton, frameTransforms);

            foreach (var bone in model.Skeleton.Bones.OrderBy(bone => bone.Index))
            {
                WriteSmdBoneTransform(writer, bone.Index, sourceTransforms[bone.Index]);
            }
        }

        writer.WriteLine("end");
    }

    private static int GetLastAnimationFrame(AnimationClip clip, IEnumerable<BoneTransformTrack> tracks)
    {
        var maxFrame = tracks
            .SelectMany(track => track.Keyframes)
            .Select(keyframe => ToAnimationFrame(keyframe.TimeSeconds, clip.FrameRate))
            .DefaultIfEmpty(0)
            .Max();
        var durationFrame = ToAnimationFrame(clip.DurationSeconds, clip.FrameRate);

        return Math.Max(0, Math.Max(maxFrame, durationFrame));
    }

    private static Transform GetFrameTransform(Bone bone, BoneTransformTrack? track, int frameIndex, float frameRate)
    {
        if (track is null || track.Keyframes.Count == 0)
        {
            return bone.LocalBindPose;
        }

        Transform transform = bone.LocalBindPose;
        foreach (var keyframe in track.Keyframes.OrderBy(keyframe => keyframe.TimeSeconds))
        {
            if (ToAnimationFrame(keyframe.TimeSeconds, frameRate) > frameIndex)
            {
                break;
            }

            transform = keyframe.Transform;
        }

        return transform;
    }

    private static int ToAnimationFrame(float timeSeconds, float frameRate)
    {
        return (int)MathF.Round(timeSeconds * frameRate);
    }

    private static void WriteSmdNodes(StreamWriter writer, Skeleton? skeleton)
    {
        writer.WriteLine("nodes");

        if (skeleton is null || skeleton.Bones.Count == 0)
        {
            writer.WriteLine("0 \"root\" -1");
        }
        else
        {
            foreach (var bone in skeleton.Bones.OrderBy(bone => bone.Index))
            {
                writer.WriteLine(FormattableString.Invariant(
                    $"{bone.Index} \"{EscapeSmdString(bone.Name)}\" {bone.ParentIndex}"));
            }
        }

        writer.WriteLine("end");
    }

    private static void WriteReferenceSkeleton(StreamWriter writer, Skeleton? skeleton)
    {
        writer.WriteLine("skeleton");
        writer.WriteLine("time 0");

        if (skeleton is null || skeleton.Bones.Count == 0)
        {
            writer.WriteLine("0 0 0 0 0 0 0");
        }
        else
        {
            var sourceTransforms = BuildSourceLocalTransforms(skeleton);
            foreach (var bone in skeleton.Bones.OrderBy(bone => bone.Index))
            {
                WriteSmdBoneTransform(writer, bone.Index, sourceTransforms[bone.Index]);
            }
        }

        writer.WriteLine("end");
    }

    private static Dictionary<int, Transform> BuildSourceLocalTransforms(
        Skeleton skeleton,
        Dictionary<int, Transform>? localTransforms = null)
    {
        Dictionary<int, Matrix4x4> originalWorldTransforms = [];
        Dictionary<int, Matrix4x4> sourceWorldTransforms = [];
        Dictionary<int, Transform> sourceLocalTransforms = [];

        foreach (var bone in skeleton.Bones.OrderBy(bone => bone.Index))
        {
            var localTransform = localTransforms is not null && localTransforms.TryGetValue(bone.Index, out var overrideTransform)
                ? overrideTransform
                : bone.LocalBindPose;
            var parentOriginalWorld = bone.ParentIndex >= 0 && originalWorldTransforms.TryGetValue(bone.ParentIndex, out var originalParent)
                ? originalParent
                : Matrix4x4.Identity;
            var parentSourceWorld = bone.ParentIndex >= 0 && sourceWorldTransforms.TryGetValue(bone.ParentIndex, out var sourceParent)
                ? sourceParent
                : Matrix4x4.Identity;
            var originalWorld = ToTransformMatrix(localTransform, includeScale: true) * parentOriginalWorld;

            Matrix4x4.Invert(parentSourceWorld, out var inverseParentSourceWorld);
            var sourceTranslation = Vector3.Transform(originalWorld.Translation, inverseParentSourceWorld);
            var sourceLocalTransform = new Transform(sourceTranslation, localTransform.Rotation, Vector3.One);
            var sourceWorld = ToTransformMatrix(sourceLocalTransform, includeScale: false) * parentSourceWorld;

            originalWorldTransforms[bone.Index] = originalWorld;
            sourceLocalTransforms[bone.Index] = sourceLocalTransform;
            sourceWorldTransforms[bone.Index] = sourceWorld;
        }

        return sourceLocalTransforms;
    }

    private static Matrix4x4 ToTransformMatrix(Transform transform, bool includeScale)
    {
        var matrix = Matrix4x4.CreateFromQuaternion(NormalizeQuaternion(transform.Rotation)) *
            Matrix4x4.CreateTranslation(transform.Translation);

        return includeScale
            ? Matrix4x4.CreateScale(transform.Scale) * matrix
            : matrix;
    }

    private static void WriteSmdBoneTransform(StreamWriter writer, int boneIndex, Transform transform)
    {
        var rotation = ToEulerRadians(transform.Rotation);
        var translation = transform.Translation * _metersToSourceUnits;
        writer.WriteLine(FormattableString.Invariant(
            $"{boneIndex} {translation.X:0.######} {translation.Y:0.######} {translation.Z:0.######} {rotation.X:0.######} {rotation.Y:0.######} {rotation.Z:0.######}"));
    }

    private static Vector3 ToEulerRadians(Quaternion rotation)
    {
        rotation = NormalizeQuaternion(rotation);
        double x = rotation.X;
        double y = rotation.Y;
        double z = rotation.Z;
        double w = rotation.W;

        // Mirrors Source's MatrixAngles: read the angles from the rotation matrix's forward and left
        // columns. At ±90° pitch roll and yaw spin about the same axis, so the quaternion formulas
        // degenerate to atan2(0, 0); fold the whole spin into yaw instead of emitting noise.
        var forwardX = 1.0 - 2.0 * (y * y + z * z);
        var forwardY = 2.0 * (x * y + w * z);
        var forwardZ = 2.0 * (x * z - w * y);
        var leftX = 2.0 * (x * y - w * z);
        var leftY = 1.0 - 2.0 * (x * x + z * z);
        var leftZ = 2.0 * (y * z + w * x);
        var upZ = 1.0 - 2.0 * (x * x + y * y);

        var forwardLength = Math.Sqrt(forwardX * forwardX + forwardY * forwardY);
        var pitch = Math.Atan2(-forwardZ, forwardLength);
        double roll;
        double yaw;
        if (forwardLength > _gimbalLockThreshold)
        {
            roll = Math.Atan2(leftZ, upZ);
            yaw = Math.Atan2(forwardY, forwardX);
        }
        else
        {
            roll = 0.0;
            yaw = Math.Atan2(-leftX, leftY);
        }

        return new Vector3((float)roll, (float)pitch, (float)yaw);
    }

    private static Quaternion NormalizeQuaternion(Quaternion rotation)
    {
        return rotation.LengthSquared() <= 0.000001f ? Quaternion.Identity : Quaternion.Normalize(rotation);
    }

    private static string EscapeSmdString(string value)
    {
        return value.Replace("\"", "'", StringComparison.Ordinal);
    }

    private static string EscapeQcString(string value)
    {
        return value.Replace("\"", "'", StringComparison.Ordinal);
    }

    private void ExportSourceMaterials(Model model, string materialDirectory, string materialRelativeDirectory)
    {
        foreach (var material in AllMaterials(model))
        {
            if (material.DiffuseTexture is null)
            {
                continue;
            }

            var pngPath = Path.Combine(materialDirectory, $"{material.Name}.png");
            var vmtPath = Path.Combine(materialDirectory, $"{material.Name}.vmt");
            var sourceTexturePath = $"{materialRelativeDirectory}/{material.Name}".Replace('\\', '/');

            // Diffuse: written without the phong mask in alpha. The phong mask now lives in the
            // spec texture's alpha (via ToSourcePhongExponent), which means basetexture alpha is
            // free for $translucent — so glass parts can be transparent AND phong-lit at the
            // same time.
            SourceMaterialEmission.BaseTexture(material, _textureFactory).WritePng(pngPath);

            // Normal map gets the envmap mask packed into its alpha channel when both are
            // available: Source's VertexLitGeneric won't accept a separate $envmapmask alongside
            // $bumpmap (pixel-shader register conflict), so we must use $normalmapalphaenvmapmask
            // for envmap masking on bump-mapped materials.
            var specForMask = GetSourcePhongExponent(material);
            var normalTextureForWrite = material.NormalTexture is not null && specForMask is not null
                ? material.NormalTexture.WithMaskInAlpha(specForMask, _textureFactory)
                : material.NormalTexture;
            normalTextureForWrite?.WritePng(Path.Combine(materialDirectory, $"{material.Name}_normal.png"));
            specForMask?.WritePng(Path.Combine(materialDirectory, $"{material.Name}_spec.png"));

            using var writer = new StreamWriter(vmtPath, false, _utf8NoBom);
            writer.WriteLine("\"VertexLitGeneric\"");
            writer.WriteLine("{");
            writer.WriteLine(FormattableString.Invariant($"    \"$basetexture\" \"{sourceTexturePath}\""));
            writer.WriteLine("    \"$nocull\" \"1\"");
            WriteSurfaceProp(writer, material);

            if (material.NormalTexture is not null)
            {
                writer.WriteLine(FormattableString.Invariant($"    \"$bumpmap\" \"{sourceTexturePath}_normal\""));
            }

            SourceMaterialBlend.Write(writer, material);

            if (UseSourcePhong(material))
            {
                WritePhongParameters(writer, $"{sourceTexturePath}_spec", material);
                // Glass-like surfaces in Fortnite are often opaque black with high specular response
                // rather than literal transparency — the reflection sells the glassiness. We emit
                // an envmap whenever a material has spec data, masked per-pixel either from the
                // normal map's alpha channel (when a bumpmap is present — Source requires this) or
                // from the standalone spec texture's RGB.
                WriteEnvmapParameters(
                    writer,
                    standaloneMaskPath: material.NormalTexture is null ? $"{sourceTexturePath}_spec" : null,
                    normalMapAlphaMask: material.NormalTexture is not null);
            }

            IReadOnlyList<SourceMaterialEmission.ExtraTexture> extraTextures =
                [.. SourceMaterialEmission.ExtraTextures(material, _textureFactory), .. SourceMaterialDetail.ExtraTextures(material, _textureFactory)];
            SourceMaterialDetail.Write(writer, material, suffix => ExtraTexturePath(materialRelativeDirectory, material, extraTextures, suffix));
            SourceMaterialEmission.Write(writer, material, suffix => ExtraTexturePath(materialRelativeDirectory, material, extraTextures, suffix));

            SourceMaterialProxies.Write(writer, material);
            writer.WriteLine("}");

            foreach (var extra in extraTextures)
            {
                extra.Texture.WritePng(Path.Join(materialDirectory, $"{ExtraTextureBasename(material, extra)}.png"));
            }
        }
    }

    private static string ExtraTextureBasename(Material material, SourceMaterialEmission.ExtraTexture extra)
    {
        return extra.SharedBasename ?? $"{material.Name}{extra.Suffix}";
    }

    private static string ExtraTexturePath(
        string materialRelativeDirectory,
        Material material,
        IReadOnlyList<SourceMaterialEmission.ExtraTexture> extraTextures,
        string suffix)
    {
        var extra = extraTextures.First(texture => texture.Suffix == suffix);
        return $"{materialRelativeDirectory}/{ExtraTextureBasename(material, extra)}".Replace('\\', '/');
    }

    // The meshes' own materials followed by every skin's replacements, each name once.
    private static IEnumerable<Material> AllMaterials(Model model)
    {
        return model.Materials
            .Concat(model.Skins?.SelectMany(skin => skin.Replacements.Values) ?? [])
            .DistinctBy(material => material.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static void WriteSurfaceProp(StreamWriter writer, Material material)
    {
        var surfaceProp = SourceMaterialSurfaceProps.For(material);
        if (surfaceProp is not null)
        {
            writer.WriteLine(FormattableString.Invariant($"    \"$surfaceprop\" \"{surfaceProp}\""));
        }
    }

    private static bool UseSourcePhong(Material material)
    {
        // Translucent materials can now coexist with phong because the phong mask lives in the
        // spec texture's alpha rather than the basetexture alpha.
        return material.DiffuseTexture is not null && material.SpecularTexture is not null;
    }

    private Texture? GetSourcePhongExponent(Material material)
    {
        return material.SpecularTexturePacking == MaterialSpecularTexturePacking.SpecularMetallicRoughness
            ? material.SpecularTexture?.ToSourcePhongExponent(_textureFactory)
            : material.SpecularTexture;
    }

    private static void WritePhongParameters(StreamWriter writer, string specularTexturePath, Material material)
    {
        var settings = SourcePhongSettings.For(material);

        writer.WriteLine("    \"$phong\" \"1\"");
        writer.WriteLine(FormattableString.Invariant($"    \"$phongexponenttexture\" \"{specularTexturePath}\""));
        writer.WriteLine(FormattableString.Invariant($"    \"$phongboost\" \"{settings.Boost}\""));
        writer.WriteLine(FormattableString.Invariant($"    \"$phongexponent\" \"{settings.Exponent}\""));
        writer.WriteLine(FormattableString.Invariant($"    \"$phongfresnelranges\" \"{settings.FresnelRanges}\""));
    }

    // Source environment-map reflection. The reflection itself samples from `env_cubemap` entities
    // placed in the level at game time (no static cubemap baked into the asset), so the VMT just
    // references the built-in entity name. `$envmapfresnel 1` makes the reflection strongest at
    // grazing angles. Source has two mutually-exclusive paths for per-pixel envmap masking:
    //   - `$envmapmask <tex>` reads a standalone texture's RGB. Cannot coexist with `$bumpmap`.
    //   - `$normalmapalphaenvmapmask 1` reads the normal map's alpha. Required when bumpmap is set.
    // Caller picks based on whether a normal map is being emitted.
    private static void WriteEnvmapParameters(
        StreamWriter writer,
        string? standaloneMaskPath = null,
        bool normalMapAlphaMask = false)
    {
        writer.WriteLine("    \"$envmap\" \"env_cubemap\"");
        writer.WriteLine("    \"$envmapfresnel\" \"1\"");
        writer.WriteLine("    \"$envmaptint\" \"[0.5 0.5 0.5]\"");
        if (normalMapAlphaMask)
        {
            writer.WriteLine("    \"$normalmapalphaenvmapmask\" \"1\"");
        }
        else if (standaloneMaskPath is not null)
        {
            writer.WriteLine(FormattableString.Invariant($"    \"$envmapmask\" \"{standaloneMaskPath}\""));
        }
    }

    private static string[] GetMaterialDirectories(Model model, string modelPath)
    {
        var directories = model.Materials
            .SelectMany(MaterialPaths)
            .Select(NormalizeMaterialDirectory)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return directories.Length == 0 ? [GetMaterialDirectory(modelPath)] : directories;
    }

    private static IEnumerable<string> MaterialPaths(Material material)
    {
        if (!string.IsNullOrWhiteSpace(material.Path))
        {
            yield return material.Path;
        }

        foreach (var texture in material.Textures)
        {
            if (!string.IsNullOrWhiteSpace(texture.Path))
            {
                yield return texture.Path;
            }
        }
    }

    private static string GetMaterialDirectory(string modelPath)
    {
        var normalized = modelPath.Replace('\\', '/');

        if (normalized.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        var directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');

        return string.IsNullOrWhiteSpace(directory) ? "models/gmconverter" : directory;
    }

    private static string NormalizeMaterialDirectory(string materialDirectory)
    {
        var normalized = materialDirectory.Replace('\\', '/').Trim('/');

        if (normalized.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["materials/".Length..];
        }

        return normalized;
    }

    private sealed record MDLExportResult(
        string QcPath,
        string SmdPath,
        string? PhysicsSmdPath,
        string MaterialDirectory,
        string MaterialRelativeDirectory);
}

internal sealed record MDLExportOptions(
    string ModelPath,
    string? StudioMdlPath,
    string? VtfCmdPath,
    bool BuildMaterials,
    PhysicsOptions? Physics,
    MaterialOptimizationOptions? MaterialOptimization = null);
