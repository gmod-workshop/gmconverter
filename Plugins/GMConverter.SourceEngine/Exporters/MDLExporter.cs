using System.Numerics;
using System.Text;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
using GMConverter.SDK.Physics;
using GMConverter.SDK.Textures;
using GMConverter.SourceEngine.Animation;
using GMConverter.SourceEngine.Materials;
using GMConverter.SourceEngine.Physics;
using GMConverter.SourceEngine.Profiles;
using GMConverter.SourceEngine.Tools;

namespace GMConverter.SourceEngine.Exporters;

/// <summary>
/// Exports a model to the Source Engine MDL format.
/// </summary>
internal sealed class MDLExporter : IModelExporter, ICollisionPreview
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

    public string Format => "mdl";

    public string DisplayName => "Source Engine";

    // Full schema declaration — three groups (Tools, Materials, Physics). The host's generic
    // options panel renders these directly; CLI registers a --mdl-<key> argument per descriptor.
    public OptionSchema OptionSchema { get; } = new(
    [
        new OptionGroup("tools", "Tools",
        [
            new OptionDescriptor("modelPath", OptionType.String, "Model path")
            {
                Description = "Output MDL path under the game models directory. Defaults to gmconverter/<name>.mdl.",
                Aliases = ["model-path"],
            },
            // Defaults point at tools already extracted next to the app, so the field shows what
            // an export would use; blank still means "find or download it at export time".
            new OptionDescriptor("studioMdlPath", OptionType.Path, "StudioMDL override")
            {
                Description = "Optional StudioMDL-CE executable. Auto-downloads to tools/ when omitted.",
                DefaultValueFactory = () => SourceToolPaths.TryFindLocalDefaults().StudioMdl,
                Aliases = ["studiomdl-path", "studiomdl"],
            },
            new OptionDescriptor("vtfCmdPath", OptionType.Path, "VTFCmd override")
            {
                Description = "Optional VTFCmd executable. Auto-downloads VTFEdit Reloaded to tools/ when omitted and materials are built.",
                DefaultValueFactory = () => SourceToolPaths.TryFindLocalDefaults().VtfCmd,
                Aliases = ["vtfcmd-path", "vtfcmd"],
            },
            new OptionDescriptor("buildMaterials", OptionType.Bool, "Build materials")
            {
                DefaultValue = true,
                Description = "Compile VTFs and VMTs alongside the MDL. Disable for mesh-only output.",
                Aliases = ["no-materials"],
            },
        ]),
        new OptionGroup("material", "Materials",
        [
            new OptionDescriptor("material:maxTextureSize", OptionType.Enum, "Max texture size")
            {
                DefaultValue = "0",
                Choices = ["0", "512", "1024", "2048", "4096"],
                Description = "Cap the longest edge before VTF compile. 0 disables resizing.",
                Aliases = ["max-texture-size"],
            },
            new OptionDescriptor("material:deduplicateTextures", OptionType.Bool, "Deduplicate identical textures")
            {
                DefaultValue = false,
                Description = "Hash resized texture content and reuse an existing VTF when materials produce byte-identical maps.",
                Aliases = ["deduplicate-textures"],
            },
        ]),
        new OptionGroup("physics", "Physics",
        [
            new OptionDescriptor("physics:enabled", OptionType.Bool, "Generate physics")
            {
                DefaultValue = false,
                Description = "Generate a simple collision mesh alongside the MDL.",
                Aliases = ["physics"],
            },
            new OptionDescriptor("physics:mode", OptionType.Enum, "Physics mode")
            {
                DefaultValue = "auto",
                Choices = ["auto", "bounds", "coacd"],
                Description = "auto: the model's own per-bone collision as a ragdoll when it has one, otherwise bounds. bounds: single AABB hull. coacd: native convex decomposition.",
                Aliases = ["physics-mode"],
            },
            new OptionDescriptor("physics:mass", OptionType.Float, "Mass (kg)")
            {
                Minimum = 0.1m,
                Maximum = 100000m,
                Increment = 10m,
                DefaultValue = 100f,
                Aliases = ["physics-mass"],
            },
            new OptionDescriptor("physics:coacdThreshold", OptionType.Float, "CoACD threshold")
            {
                Minimum = 0.0001m,
                Maximum = 1m,
                Increment = 0.001m,
                DefaultValue = 0.05f,
                Description = "CoACD termination threshold from 0.01 to 1.",
                Aliases = ["coacd-threshold"],
            },
            new OptionDescriptor("physics:maxConvexPieces", OptionType.Int, "Max convex pieces")
            {
                Minimum = -1m,
                Maximum = 128m,
                DefaultValue = 16,
                Description = "Maximum CoACD convex hull count. Use -1 for no limit.",
                Aliases = ["max-convex-pieces"],
            },
            new OptionDescriptor("physics:maxHullVertices", OptionType.Int, "Max hull vertices")
            {
                Minimum = 4m,
                Maximum = 256m,
                DefaultValue = 16,
                Aliases = ["coacd-max-hull-vertices", "max-hull-vertices"],
            },
        ]),
    ]);

    public ExportResult Export(Model model, ExportContext context)
    {
        var outputDirectory = context.OutputDirectory;
        var baseName = context.BaseName;
        var options = BuildOptions(baseName, context.Options);
        model = SourceFacing.FaceForward(model);
        var sourceTools = SourceToolPaths.Resolve(options.StudioMdlPath, options.VtfCmdPath, options.BuildMaterials);
        var physicsOptions = options.Physics;
        var modelPath = options.ModelPath;
        var safeBaseName = NameHelpers.SanitizeFileName(baseName);
        var smdPath = Path.Combine(outputDirectory, $"{safeBaseName}.smd");
        var physicsSmdPath =
            physicsOptions is null ? null : Path.Combine(outputDirectory, $"{safeBaseName}_phys.smd");
        var animationSmdPaths = GetAnimationSmdPaths(model, outputDirectory, safeBaseName);
        var basePoseSmdPaths = GetBasePoseSmdPaths(animationSmdPaths, outputDirectory, safeBaseName);
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

        foreach (var (pose, basePoseSmdPath) in basePoseSmdPaths)
        {
            WriteAnimationSmd(model, CreatePoseClip(pose), basePoseSmdPath);
        }

        var target = SourceProfiles.Find(context.TargetGameId);
        WriteQc(qcPath, model, modelPath, safeBaseName, materialRelativeDirectories, physicsSmdPath, physicsOptions, animationSmdPaths, basePoseSmdPaths,
            clip => target?.GetActivities(clip, context.TargetOptions) ?? [],
            clip => target?.GetEvents(clip) ?? [],
            axis => target?.GetPoseParameter(axis));

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
        return new ExportResult([qcPath]);
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
                    "bounds" => PhysicsMode.Bounds,
                    _ => PhysicsMode.Auto,
                },
                Mass: o.GetFloat("physics:mass", 100f),
                Coacd: o.GetString("physics:mode") == "coacd"
                    ? new CoacdOptions(
                        Threshold: o.GetFloat("physics:coacdThreshold", 0.05f),
                        MaxConvexPieces: o.GetInt("physics:maxConvexPieces", 16),
                        MaxHullVertices: o.GetInt("physics:maxHullVertices", 16))
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

    public IReadOnlyList<Mesh> CreateCollisionPreview(Model model, OptionValues options)
    {
        return BuildOptions(model.Name, options).Physics switch
        {
            null => [],
            { Mode: PhysicsMode.Coacd, Coacd: { } coacd } => DecomposeCoacd(model, coacd),
            { Mode: PhysicsMode.Auto } when GetRagdoll(model) is { } ragdoll => CreateRagdollMeshes(ragdoll),
            _ => [CreateBoundsMesh(GetPhysicsBounds(model))],
        };
    }

    // A ragdoll needs bones to hang its bodies on.
    private static (Skeleton Skeleton, ModelPhysics Physics)? GetRagdoll(Model model)
    {
        return model is { Skeleton: { Bones.Count: > 0 } skeleton, Physics: { Bodies.Count: > 0 } physics }
            ? (skeleton, physics)
            : null;
    }

    private static bool UsesRagdoll(Model model, PhysicsOptions? physicsOptions)
    {
        return physicsOptions?.Mode is PhysicsMode.Auto && GetRagdoll(model) is not null;
    }

    // Every collision shape as a convex hull in model space, tagged with its body's bone.
    private static List<(int BoneIndex, Vector3[] Points, IReadOnlyList<Triangle> Triangles)> BuildRagdollHulls(
        (Skeleton Skeleton, ModelPhysics Physics) ragdoll)
    {
        var worlds = BuildSourceWorldTransforms(ragdoll.Skeleton);
        List<(int, Vector3[], IReadOnlyList<Triangle>)> hulls = [];
        foreach (var body in ragdoll.Physics.Bodies)
        {
            if (!worlds.Original.TryGetValue(body.BoneIndex, out var boneWorld))
            {
                continue;
            }

            foreach (var shape in body.Shapes)
            {
                Vector3[] points = [.. CollisionShapePoints.Get(shape).Select(point => Vector3.Transform(point, boneWorld))];
                if (ConvexHull.Build(points) is { Count: > 0 } triangles)
                {
                    hulls.Add((body.BoneIndex, points, triangles));
                }
            }
        }

        return hulls;
    }

    private static IReadOnlyList<Mesh> CreateRagdollMeshes((Skeleton Skeleton, ModelPhysics Physics) ragdoll)
    {
        return
        [
            .. BuildRagdollHulls(ragdoll).Select(hull => new Mesh(
                [.. hull.Points.Select(point => new Vertex(point, Vector3.UnitZ, Vector2.Zero))],
                [new Submesh("physics", hull.Triangles)])),
        ];
    }

    private static Bounds GetPhysicsBounds(Model model)
    {
        return model.Bounds().WithMinimumThickness(1f / _metersToSourceUnits);
    }

    private static IReadOnlyList<Mesh> DecomposeCoacd(Model model, CoacdOptions options)
    {
        return CoacdNative.Decompose(
            model.Merge(),
            new CoacdDecompositionOptions(options.Threshold, options.MaxConvexPieces, options.MaxHullVertices));
    }

    // Twelve outward-facing triangles over the box corners, matching the bounds physics SMD.
    private static Mesh CreateBoundsMesh(Bounds bounds)
    {
        Vector3[] corners =
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
        (int A, int B, int C, int D, Vector3 Normal)[] faces =
        [
            (0, 3, 2, 1, -Vector3.UnitZ),
            (4, 5, 6, 7, Vector3.UnitZ),
            (0, 1, 5, 4, -Vector3.UnitY),
            (3, 7, 6, 2, Vector3.UnitY),
            (0, 4, 7, 3, -Vector3.UnitX),
            (1, 2, 6, 5, Vector3.UnitX),
        ];
        List<Vertex> vertices = [];
        List<Triangle> triangles = [];
        foreach (var (a, b, c, d, normal) in faces)
        {
            var start = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d }.Select(index => new Vertex(corners[index], normal, Vector2.Zero)));
            triangles.Add(new Triangle(start, start + 1, start + 2));
            triangles.Add(new Triangle(start, start + 2, start + 3));
        }
        return new Mesh(vertices, [new Submesh("physics", triangles)]);
    }

    private static void WritePhysicsSmd(Model model, string physicsSmdPath, PhysicsOptions physicsOptions)
    {
        switch (physicsOptions.Mode)
        {
            case PhysicsMode.Auto when GetRagdoll(model) is { } ragdoll:
                WriteRagdollPhysicsSmd(physicsSmdPath, ragdoll);
                break;

            case PhysicsMode.Auto:
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
        var bounds = GetPhysicsBounds(model);
        using var writer = CreatePhysicsSmdWriter(physicsSmdPath, model.Skeleton);

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
        var parts = DecomposeCoacd(model, options);

        if (parts.Count == 0)
        {
            throw new GMConverterException("CoACD did not produce any convex parts.");
        }

        WritePhysicsPartsSmd(physicsSmdPath, parts, model.Skeleton);
    }

    private static void WritePhysicsPartsSmd(string physicsSmdPath, IReadOnlyList<Mesh> parts, Skeleton? skeleton)
    {
        using var writer = CreatePhysicsSmdWriter(physicsSmdPath, skeleton);
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

    // Each hull's vertices are bound to its body's bone; studiomdl turns every bone with
    // collision into one ragdoll part and the triangles connected on it into one convex piece.
    private static void WriteRagdollPhysicsSmd(string physicsSmdPath, (Skeleton Skeleton, ModelPhysics Physics) ragdoll)
    {
        var hulls = BuildRagdollHulls(ragdoll);
        if (hulls.Count == 0)
        {
            throw new GMConverterException("The model's collision bodies did not produce any convex hulls.");
        }

        using var writer = CreatePhysicsSmdWriter(physicsSmdPath, ragdoll.Skeleton);
        foreach (var (boneIndex, points, triangles) in hulls)
        {
            foreach (var triangle in triangles)
            {
                var normal = triangle.Normal(points);
                writer.WriteLine("physics");
                WritePhysicsVertex(writer, points[triangle.A], normal, boneIndex);
                WritePhysicsVertex(writer, points[triangle.C], normal, boneIndex);
                WritePhysicsVertex(writer, points[triangle.B], normal, boneIndex);
            }
        }

        writer.WriteLine("end");
    }

    // The collision SMD must declare the same skeleton and bind pose as the reference mesh.
    // studiomdl attaches collision to the model's root bone and reads the vertices relative to
    // that bone's pose in this file, so a lone identity "root" node would leave the hulls offset
    // by whatever rotation the real root bone has (e.g. a 180-degree yaw on Men of War models).
    private static StreamWriter CreatePhysicsSmdWriter(string physicsSmdPath, Skeleton? skeleton)
    {
        var writer = new StreamWriter(physicsSmdPath, false, _utf8NoBom);
        writer.WriteLine("version 1");
        WriteSmdNodes(writer, skeleton);
        WriteReferenceSkeleton(writer, skeleton);
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

    private static void WritePhysicsVertex(StreamWriter writer, Vector3 vertex, Vector3 normal, int boneIndex = 0)
    {
        var scaled = vertex * _metersToSourceUnits;
        writer.WriteLine(FormattableString.Invariant(
            $"{boneIndex} {scaled.X:0.######} {scaled.Y:0.######} {scaled.Z:0.######} {normal.X:0.######} {normal.Y:0.######} {normal.Z:0.######} 0 0"));
    }

    private static void WriteQc(
        string qcPath,
        Model model,
        string modelPath,
        string safeBaseName,
        IReadOnlyList<string> materialRelativeDirectories,
        string? physicsSmdPath,
        PhysicsOptions? physicsOptions,
        (AnimationClip Clip, string SmdPath)[] animationSmdPaths,
        Dictionary<AnimationPose, string> basePoseSmdPaths,
        Func<AnimationClip, IReadOnlyList<SourceActivity>> getActivities,
        Func<AnimationClip, IReadOnlyList<SourceAnimationEvent>> getEvents,
        Func<BlendAxis, SourcePoseParameter?> getPoseParameter)
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
        WriteAttachments(writer, model);
        WriteEyePosition(writer, model);
        WriteHitboxes(writer, model);
        WriteIkChains(writer, model);

        writer.WriteLine("$sequence \"idle\" \"{0}\" fps 1", smdFileName);

        // Base poses are plain $animations, so they can be subtracted without showing up as sequences.
        foreach (var basePoseSmdPath in basePoseSmdPaths.Values)
        {
            writer.WriteLine(FormattableString.Invariant(
                $"$animation \"{GetBasePoseAnimationName(basePoseSmdPath)}\" \"{Path.GetFileName(basePoseSmdPath)}\""));
        }

        foreach (var (clip, animationSmdPath) in animationSmdPaths)
        {
            // Game code plays sequences by activity. A clip with several activities is written once
            // per activity, since a sequence holds only one.
            var activities = getActivities(clip);
            var events = getEvents(clip);
            if (activities.Count == 0)
            {
                WriteSequence(writer, model, clip, animationSmdPath, clip.Name, activity: null, events, basePoseSmdPaths);
                continue;
            }

            foreach (var activity in activities)
            {
                WriteSequence(writer, model, clip, animationSmdPath, clip.Name + activity.SequenceSuffix, activity.Name, events, basePoseSmdPaths);
            }
        }

        // Blend spaces become blended sequences on the pose parameters game code sets.
        var blendSequences = SourceBlendSequences.Plan(model, animationSmdPaths, getPoseParameter);
        if (blendSequences.Count > 0)
        {
            SourceBlendSequences.WritePoseParameters(writer, blendSequences);
            foreach (var blendSequence in blendSequences)
            {
                SourceBlendSequences.Write(writer, blendSequence, pose => pose is { } basePose
                    ? GetBasePoseAnimationName(basePoseSmdPaths[basePose])
                    : "idle");
            }
        }

        if (physicsSmdPath is not null && UsesRagdoll(model, physicsOptions))
        {
            WriteCollisionJoints(writer, model, Path.GetFileName(physicsSmdPath), physicsOptions!.Mass);
        }
        else if (physicsSmdPath is not null)
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

    private static void WriteSequence(
        StreamWriter writer,
        Model model,
        AnimationClip clip,
        string animationSmdPath,
        string sequenceName,
        string? activity,
        IReadOnlyList<SourceAnimationEvent> events,
        Dictionary<AnimationPose, string> basePoseSmdPaths)
    {
        var playbackOptions = GetPlaybackOptions(model, clip);
        if (activity is not null)
        {
            playbackOptions.Insert(0, $"activity {activity} 1");
        }

        playbackOptions.AddRange(GetFootstepRules(model, clip));

        var lastFrame = GetLastAnimationFrame(clip, clip.Tracks.OfType<BoneTransformTrack>());
        foreach (var animationEvent in events)
        {
            var frame = Math.Clamp(ToAnimationFrame(animationEvent.TimeSeconds, clip.FrameRate), 0, Math.Max(lastFrame, 0));
            playbackOptions.Add(FormattableString.Invariant(
                $"{{ event {animationEvent.Name} {frame} \"{EscapeQcString(animationEvent.Options)}\" }}"));
        }

        if (clip.Layering is null && playbackOptions.Count == 0)
        {
            writer.WriteLine(FormattableString.Invariant(
                $"$sequence \"{EscapeQcString(sequenceName)}\" \"{Path.GetFileName(animationSmdPath)}\" fps {clip.FrameRate:0.######}"));
            return;
        }

        writer.WriteLine(FormattableString.Invariant($"$sequence \"{EscapeQcString(sequenceName)}\" {{"));
        writer.WriteLine(FormattableString.Invariant($"    \"{Path.GetFileName(animationSmdPath)}\""));
        writer.WriteLine(FormattableString.Invariant($"    fps {clip.FrameRate:0.######}"));
        if (clip.Layering is { } layering)
        {
            // A layer becomes a delta sequence: studiomdl subtracts the base pose in parent-local
            // space, and `delta` makes the engine add the result on top of the playing animation.
            // Source has no model-space deltas (`worldspace` is an absolute override that ignores
            // `delta`), so model-space layers are also written as local ones. They match exactly on
            // the pose they were authored against and approximate it on others.
            var baseName = layering.Base is { } basePose
                ? GetBasePoseAnimationName(basePoseSmdPaths[basePose])
                : "idle";
            writer.WriteLine(FormattableString.Invariant($"    subtract \"{EscapeQcString(baseName)}\" 0"));
            writer.WriteLine("    delta");
        }

        foreach (var option in playbackOptions)
        {
            writer.WriteLine("    " + option);
        }

        writer.WriteLine("}");
    }

    private static List<string> GetPlaybackOptions(Model model, AnimationClip clip)
    {
        List<string> options = [.. GetMotionExtraction(model, clip)];
        if (clip.Loops)
        {
            options.Add("loop");
        }

        return options;
    }

    // Root motion is taken out of the root bone and stored as the sequence's movement, which is
    // what the engine moves NPCs and players by. A loop is one steady stride, so one linear piece
    // fits it; starts, stops and pivots speed up and slow down, so they are cut into pieces of
    // about a quarter of a second (studiomdl's `walkframe`) to keep that profile. Turning is
    // extracted too (LZR) only when the root actually turns. Layers never move the character.
    private static IEnumerable<string> GetMotionExtraction(Model model, AnimationClip clip)
    {
        if (GetRootMotionTrack(model, clip) is not { } rootTrack)
        {
            yield break;
        }

        var lastFrame = GetLastAnimationFrame(clip, clip.Tracks.OfType<BoneTransformTrack>());
        if (lastFrame < 1)
        {
            yield break;
        }

        var firstRotation = rootTrack.Keyframes[0].Transform.Rotation;
        var turns = rootTrack.Keyframes.Any(keyframe => MathF.Abs(GetYawChangeDegrees(firstRotation, keyframe.Transform.Rotation)) > 1f);
        var axes = turns ? "LX LY LZR" : "LX LY";
        if (clip.Loops)
        {
            yield return axes;
            yield break;
        }

        var pieces = Math.Clamp((int)MathF.Ceiling(lastFrame / (clip.FrameRate * 0.25f)), 1, 32);
        var previousFrame = 0;
        for (var piece = 1; piece <= pieces; piece++)
        {
            var endFrame = (int)MathF.Round(lastFrame * (piece / (float)pieces));
            if (endFrame > previousFrame)
            {
                yield return FormattableString.Invariant($"walkframe {endFrame} {axes}");
                previousFrame = endFrame;
            }
        }
    }

    // Source names its IK chains after the limb, as in Valve's own characters. Each chain is the end
    // bone and its two parents; studiomdl works out which way the knee bends from the animations.
    // The feet are locked while autoplay layers (aim offsets) play, so bending the body to aim
    // doesn't drag them across the ground.
    private static void WriteIkChains(StreamWriter writer, Model model)
    {
        if (GetIkChains(model) is not { Count: > 0 } chains)
        {
            return;
        }

        foreach (var (name, chain, bone) in chains)
        {
            writer.WriteLine(FormattableString.Invariant($"$ikchain \"{name}\" \"{EscapeQcString(bone.Name)}\""));
        }

        foreach (var (name, _, _) in chains.Where(chain => IsFoot(chain.Chain)))
        {
            writer.WriteLine(FormattableString.Invariant($"$ikautoplaylock \"{name}\" 1 0.1"));
        }
    }

    // A footstep rule for every stretch of the clip where a foot is planted, so the engine keeps it
    // on the ground under it. Layers only adjust another sequence's pose, so they get none.
    private static IEnumerable<string> GetFootstepRules(Model model, AnimationClip clip)
    {
        if (clip.Layering is not null || model.Skeleton is not { } skeleton)
        {
            yield break;
        }

        var feet = GetIkChains(model).Where(chain => IsFoot(chain.Chain)).ToArray();
        if (feet.Length == 0)
        {
            yield break;
        }

        var tracksByBone = clip.Tracks
            .OfType<BoneTransformTrack>()
            .GroupBy(track => track.BoneIndex)
            .ToDictionary(group => group.Key, group => group.First());
        var lastFrame = GetLastAnimationFrame(clip, tracksByBone.Values);
        var positions = feet.Select(_ => new List<Vector3>()).ToArray();
        for (var frameIndex = 0; frameIndex <= lastFrame; frameIndex++)
        {
            var frameTransforms = skeleton.Bones.ToDictionary(
                bone => bone.Index,
                bone => GetFrameTransform(bone, tracksByBone.GetValueOrDefault(bone.Index), frameIndex, clip.FrameRate));
            Dictionary<int, Matrix4x4> worlds = [];
            BuildSourceLocalTransforms(skeleton, frameTransforms, worlds);
            for (var foot = 0; foot < feet.Length; foot++)
            {
                positions[foot].Add(worlds[feet[foot].Bone.Index].Translation);
            }
        }

        for (var foot = 0; foot < feet.Length; foot++)
        {
            foreach (var plant in SourceFootPlants.Find(positions[foot], clip.FrameRate, clip.Loops))
            {
                yield return FormattableString.Invariant(
                    $"ikrule \"{feet[foot].Name}\" footstep range {plant.Start} {plant.Peak} {plant.Tail} {plant.End} contact {plant.Contact}");
            }
        }
    }

    private static List<(string Name, IkChain Chain, Bone Bone)> GetIkChains(Model model)
    {
        if (model is not { Skeleton: { } skeleton, IkChains: { } chains })
        {
            return [];
        }

        var bones = skeleton.Bones.ToDictionary(bone => bone.Index);
        return
        [
            .. chains
                .Where(chain => bones.TryGetValue(chain.EndBoneIndex, out var end) &&
                    bones.TryGetValue(end.ParentIndex, out var middle) &&
                    bones.ContainsKey(middle.ParentIndex))
                .DistinctBy(chain => chain.Kind)
                .Select(chain => (GetIkChainName(chain.Kind), chain, bones[chain.EndBoneIndex])),
        ];
    }

    private static string GetIkChainName(IkChainKind kind) => kind switch
    {
        IkChainKind.LeftFoot => "lfoot",
        IkChainKind.RightFoot => "rfoot",
        IkChainKind.LeftHand => "lhand",
        _ => "rhand",
    };

    private static bool IsFoot(IkChain chain) => chain.Kind is IkChainKind.LeftFoot or IkChainKind.RightFoot;

    // The root's track when the clip's root motion is extracted, or null when it isn't.
    private static BoneTransformTrack? GetRootMotionTrack(Model model, AnimationClip clip)
    {
        return clip.RootMotion &&
            clip.Layering is null &&
            model.Skeleton?.Bones.FirstOrDefault(bone => bone.ParentIndex < 0) is { } root &&
            clip.Tracks.OfType<BoneTransformTrack>().FirstOrDefault(track => track.BoneIndex == root.Index) is { Keyframes.Count: > 1 } rootTrack
                ? rootTrack
                : null;
    }

    // Extraction only takes out the root's motion from its first frame on, so wherever the root
    // starts stays in the pose. Unreal stops and pivots are authored to end on the origin and start
    // metres away (and turned), which would draw the model off to the side for the whole sequence.
    // Like Unreal's root lock, the root is moved so the first frame sits on its bind pose, and the
    // rest of the clip keeps its motion relative to that.
    private static Func<Transform, Transform> CreateRootMotionRebase(Bone root, BoneTransformTrack rootTrack, float frameRate)
    {
        var first = ToTransformMatrix(GetFrameTransform(root, rootTrack, 0, frameRate), includeScale: true);
        Matrix4x4.Invert(first, out var inverseFirst);
        var correction = inverseFirst * ToTransformMatrix(root.LocalBindPose, includeScale: true);
        return transform =>
        {
            Matrix4x4.Decompose(ToTransformMatrix(transform, includeScale: true) * correction, out var scale, out var rotation, out var translation);
            return new Transform(translation, rotation, scale);
        };
    }

    // The turn about the model's vertical axis between two root rotations.
    private static float GetYawChangeDegrees(Quaternion from, Quaternion to)
    {
        var forward = Vector3.Transform(Vector3.UnitX, NormalizeQuaternion(to) * Quaternion.Inverse(NormalizeQuaternion(from)));
        return MathF.Atan2(forward.Y, forward.X) * (180f / MathF.PI);
    }

    // The damping and inertia values are the ones Valve's own character ragdolls use.
    private static void WriteCollisionJoints(StreamWriter writer, Model model, string physicsSmdFileName, float mass)
    {
        var (skeleton, physics) = GetRagdoll(model)!.Value;
        writer.WriteLine(FormattableString.Invariant($"$collisionjoints \"{physicsSmdFileName}\""));
        writer.WriteLine("{");
        writer.WriteLine(FormattableString.Invariant($"    $mass {mass:0.###}"));
        writer.WriteLine("    $inertia 10");
        writer.WriteLine("    $damping 0.01");
        writer.WriteLine("    $rotdamping 1.5");
        if (physics.Bodies.Any(body => body.Shapes.Count > 1))
        {
            writer.WriteLine("    $concave");
        }

        var worlds = BuildSourceWorldTransforms(skeleton);
        foreach (var joint in physics.Joints)
        {
            var bone = skeleton.Bones.FirstOrDefault(bone => bone.Index == joint.BoneIndex);
            if (bone is null)
            {
                continue;
            }

            var limits = MapJointLimits(joint, worlds.Original[bone.Index], worlds.Source[bone.Index]);
            foreach (var (axis, limit) in new[] { ("x", limits[0]), ("y", limits[1]), ("z", limits[2]) })
            {
                // Free axes get a full turn each way; studiomdl parses the `limit` form the same
                // way in every version.
                var (min, max) = limit is null ? (-180f, 180f) : (limit.MinDegrees, limit.MaxDegrees);
                writer.WriteLine(FormattableString.Invariant(
                    $"    $jointconstrain \"{EscapeQcString(bone.Name)}\" {axis} limit {MathF.Round(min, 2) + 0f} {MathF.Round(max, 2) + 0f} 0"));
            }
        }

        writer.WriteLine("}");
    }

    // Source limits rotation about the child bone's own axes, while the joint's limits are about
    // its frame's axes. Each bone axis takes the limit of the frame axis it lines up with best
    // (as a whole permutation), with the range flipped when the two point opposite ways.
    private static AngularLimit?[] MapJointLimits(PhysicsJoint joint, Matrix4x4 originalBoneWorld, Matrix4x4 sourceBoneWorld)
    {
        Matrix4x4.Invert(sourceBoneWorld, out var inverseSourceBoneWorld);
        Vector3 ToSourceBone(Vector3 axis) => Vector3.Normalize(Vector3.TransformNormal(
            Vector3.TransformNormal(Vector3.Transform(axis, joint.Frame), originalBoneWorld), inverseSourceBoneWorld));
        Vector3[] frameAxes = [ToSourceBone(Vector3.UnitX), ToSourceBone(Vector3.UnitY), ToSourceBone(Vector3.UnitZ)];
        AngularLimit?[] frameLimits = [joint.X, joint.Y, joint.Z];
        Vector3[] boneAxes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];

        int[][] permutations = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        var best = permutations.MaxBy(permutation =>
            Enumerable.Range(0, 3).Sum(axis => MathF.Abs(Vector3.Dot(boneAxes[axis], frameAxes[permutation[axis]]))))!;
        return
        [
            .. Enumerable.Range(0, 3).Select(axis =>
            {
                var limit = frameLimits[best[axis]];
                return limit is not null && Vector3.Dot(boneAxes[axis], frameAxes[best[axis]]) < 0f
                    ? new AngularLimit(-limit.MaxDegrees, -limit.MinDegrees)
                    : limit;
            }),
        ];
    }

    // Hitboxes are the bounds of each body's hulls in its Source bone's space, so they match the
    // ragdoll's shapes. Models without bodies are left to studiomdl's generated hitboxes.
    private static void WriteHitboxes(StreamWriter writer, Model model)
    {
        if (GetRagdoll(model) is not { } ragdoll)
        {
            return;
        }

        var worlds = BuildSourceWorldTransforms(ragdoll.Skeleton);
        var hitboxes = BuildRagdollHulls(ragdoll)
            .GroupBy(hull => hull.BoneIndex)
            .Select(group =>
            {
                Matrix4x4.Invert(worlds.Source[group.Key], out var inverseSourceBoneWorld);
                var bounds = Bounds.FromPoints([.. group
                    .SelectMany(hull => hull.Points)
                    .Select(point => Vector3.Transform(point, inverseSourceBoneWorld) * _metersToSourceUnits)]);
                return (Bone: ragdoll.Skeleton.Bones.First(bone => bone.Index == group.Key), Bounds: bounds);
            })
            .ToArray();
        if (hitboxes.Length == 0)
        {
            return;
        }

        static float Clean(float value) => MathF.Round(value, 3) + 0f;
        writer.WriteLine("$hboxset \"default\"");
        foreach (var (bone, bounds) in hitboxes)
        {
            writer.WriteLine(FormattableString.Invariant(
                $"$hbox {SourceHitGroups.FromBoneName(bone.Name)} \"{EscapeQcString(bone.Name)}\" {Clean(bounds.Min.X)} {Clean(bounds.Min.Y)} {Clean(bounds.Min.Z)} {Clean(bounds.Max.X)} {Clean(bounds.Max.Y)} {Clean(bounds.Max.Z)}"));
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

    // One SMD per distinct base pose. Clips that share a base share the AnimationPose instance.
    private static Dictionary<AnimationPose, string> GetBasePoseSmdPaths(
        (AnimationClip Clip, string SmdPath)[] animationSmdPaths,
        string outputDirectory,
        string safeBaseName)
    {
        Dictionary<AnimationPose, string> paths = new(ReferenceEqualityComparer.Instance);
        var basePoses = animationSmdPaths
            .Select(entry => entry.Clip.Layering?.Base)
            .OfType<AnimationPose>()
            .Distinct<AnimationPose>(ReferenceEqualityComparer.Instance);
        foreach (var basePose in basePoses)
        {
            paths.Add(basePose, Path.Join(
                outputDirectory,
                $"{safeBaseName}_base{paths.Count}_{NameHelpers.SanitizeFileName(basePose.Name)}.smd"));
        }

        return paths;
    }

    private static string GetBasePoseAnimationName(string basePoseSmdPath)
    {
        return Path.GetFileNameWithoutExtension(basePoseSmdPath);
    }

    private static AnimationClip CreatePoseClip(AnimationPose pose)
    {
        return new AnimationClip(
            pose.Name,
            30f,
            0f,
            [.. pose.BoneTransforms.Select(bone => new BoneTransformTrack(bone.Key, [new TransformKeyframe(0f, bone.Value)]))]);
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
        var root = model.Skeleton.Bones.FirstOrDefault(bone => bone.ParentIndex < 0);
        var rebaseRoot = root is not null && GetRootMotionTrack(model, clip) is { } rootTrack
            ? CreateRootMotionRebase(root, rootTrack, clip.FrameRate)
            : null;

        for (var frameIndex = 0; frameIndex <= lastFrame; frameIndex++)
        {
            writer.WriteLine(FormattableString.Invariant($"time {frameIndex}"));
            var frameTransforms = model.Skeleton.Bones.ToDictionary(
                bone => bone.Index,
                bone => GetFrameTransform(bone, tracksByBone.GetValueOrDefault(bone.Index), frameIndex, clip.FrameRate));
            if (rebaseRoot is not null)
            {
                frameTransforms[root!.Index] = rebaseRoot(frameTransforms[root.Index]);
            }
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

    // Attachments are written relative to their bone as it ends up in the SMD. Source bones carry
    // no scale (BuildSourceLocalTransforms folds it into child offsets), so the attachment's
    // placement is resolved through the original skeleton and re-expressed in the Source bone.
    private static void WriteAttachments(StreamWriter writer, Model model)
    {
        if (model.Attachments is not { Count: > 0 } attachments)
        {
            return;
        }

        var skeleton = model.Skeleton is { Bones.Count: > 0 } existing ? existing : null;
        (Dictionary<int, Matrix4x4> Original, Dictionary<int, Matrix4x4> Source)? worlds = skeleton is null ? null : BuildSourceWorldTransforms(skeleton);
        var rootIndex = skeleton?.Bones.FirstOrDefault(bone => bone.ParentIndex < 0)?.Index ?? 0;
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (var attachment in attachments)
        {
            if (!names.Add(attachment.Name))
            {
                continue;
            }

            var boneIndex = skeleton is null ? 0 : attachment.BoneIndex ?? rootIndex;
            var boneName = skeleton?.Bones.FirstOrDefault(bone => bone.Index == boneIndex)?.Name ?? "root";
            var local = ToTransformMatrix(attachment.LocalTransform, includeScale: false);
            if (worlds is not null)
            {
                var originalBoneWorld = attachment.BoneIndex is null ? Matrix4x4.Identity : worlds.Value.Original[boneIndex];
                Matrix4x4.Invert(worlds.Value.Source[boneIndex], out var inverseSourceBoneWorld);
                local = local * originalBoneWorld * inverseSourceBoneWorld;
            }

            Matrix4x4.Decompose(local, out _, out var rotation, out var translation);
            // Rounded (and -0 folded to 0) so float noise doesn't show up as 90.00001 or -0.
            var position = translation * _metersToSourceUnits;
            var angles = ToEulerRadians(rotation) * (180f / MathF.PI);
            static float Clean(float value, int digits) => MathF.Round(value, digits) + 0f;
            writer.WriteLine(FormattableString.Invariant(
                $"$attachment \"{EscapeQcString(attachment.Name)}\" \"{EscapeQcString(boneName)}\" {Clean(position.X, 4)} {Clean(position.Y, 4)} {Clean(position.Z, 4)} rotate {Clean(angles.Y, 3)} {Clean(angles.Z, 3)} {Clean(angles.X, 3)}"));
        }
    }

    // Game code places an NPC's view at $eyeposition, so it follows the model's eyes attachment
    // (in the bind pose) when the model has one.
    private static void WriteEyePosition(StreamWriter writer, Model model)
    {
        if (model.Attachments?.FirstOrDefault(attachment => string.Equals(attachment.Name, "eyes", StringComparison.OrdinalIgnoreCase)) is not { } eyes)
        {
            return;
        }

        var local = ToTransformMatrix(eyes.LocalTransform, includeScale: false);
        var world = eyes.BoneIndex is { } boneIndex && model.Skeleton is { } skeleton
            ? local * BuildSourceWorldTransforms(skeleton).Original[boneIndex]
            : local;
        var position = world.Translation * _metersToSourceUnits;
        static float Clean(float value) => MathF.Round(value, 4) + 0f;
        writer.WriteLine(FormattableString.Invariant($"$eyeposition {Clean(position.X)} {Clean(position.Y)} {Clean(position.Z)}"));
    }

    private static (Dictionary<int, Matrix4x4> Original, Dictionary<int, Matrix4x4> Source) BuildSourceWorldTransforms(Skeleton skeleton)
    {
        Dictionary<int, Matrix4x4> original = [];
        Dictionary<int, Matrix4x4> source = [];
        BuildSourceLocalTransforms(skeleton, null, original, source);
        return (original, source);
    }

    private static Dictionary<int, Transform> BuildSourceLocalTransforms(
        Skeleton skeleton,
        Dictionary<int, Transform>? localTransforms = null,
        Dictionary<int, Matrix4x4>? originalWorldTransforms = null,
        Dictionary<int, Matrix4x4>? sourceWorldTransforms = null)
    {
        originalWorldTransforms ??= [];
        sourceWorldTransforms ??= [];
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
