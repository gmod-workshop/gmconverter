using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using GMConverter.Geometry;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
using GMConverter.SDK.Textures;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;
using GltfMeshBuilder = SharpGLTF.Geometry.MeshBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexTexture1, SharpGLTF.Geometry.VertexTypes.VertexEmpty>;
using GltfSkinnedMeshBuilder = SharpGLTF.Geometry.MeshBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexTexture1, SharpGLTF.Geometry.VertexTypes.VertexJoints4>;
using GltfSkinnedVertexBuilder = SharpGLTF.Geometry.VertexBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexTexture1, SharpGLTF.Geometry.VertexTypes.VertexJoints4>;
using GltfVertexBuilder = SharpGLTF.Geometry.VertexBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexTexture1, SharpGLTF.Geometry.VertexTypes.VertexEmpty>;
using ResourceWriteMode = SharpGLTF.Schema2.ResourceWriteMode;
using TextureInterpolationFilter = SharpGLTF.Schema2.TextureInterpolationFilter;
using TextureMipMapFilter = SharpGLTF.Schema2.TextureMipMapFilter;
using TextureWrapMode = SharpGLTF.Schema2.TextureWrapMode;
using WriteSettings = SharpGLTF.Schema2.WriteSettings;

namespace GMConverter.Exporters;

internal sealed class GLTFExporter : IExporter
{
    // -90° around X rotates Z-up data into Y-up: (x, y, z) → (x, z, -y).
    private static readonly Quaternion _zUpToYUpRotation =
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f);

    public string OutputFormat => "glb";

    public string OutputName => "glTF";

    public OptionSchema OptionSchema { get; } = new(
    [
        new OptionGroup("output", "Output",
        [
            new OptionDescriptor("binary", OptionType.Bool, "Binary (.glb)")
            {
                DefaultValue = true,
                Description = "Emit a single binary .glb file instead of a .gltf + sidecars.",
            },
            new OptionDescriptor("bakeUvTransforms", OptionType.Bool, "Bake UV transforms")
            {
                DefaultValue = false,
                Description = "Fold per-material UV scale/offset into mesh UVs at write time. " +
                              "Used by the in-app preview which does not honor KHR_texture_transform.",
            },
        ]),
    ]);

    public void Export(Model model, string outputDirectory, string baseName, OptionValues options)
    {
        using var exportScope = PerfTimer.Measure(
            "gltf.export",
            "Export",
            $"meshes={model.Meshes.Count} materials={model.Materials.Count} textures={model.Textures.Count}");

        var binary = options.GetBool("binary", defaultValue: true);
        var bakeUvTransforms = options.GetBool("bakeUvTransforms");

        var safeBaseName = NameHelpers.SanitizeFileName(baseName);
        var extension = binary ? ".glb" : ".gltf";
        var outputPath = Path.Combine(outputDirectory, $"{safeBaseName}{extension}");

        // Per-Export memoization: the same source Texture instance can appear in multiple Materials
        // (e.g. a normal map shared across body and arms). Calling Texture.ToPngBytes() re-runs an
        // ImageSharp PNG encode each time — for a 2K-4K texture that's 50-200 ms each. Cache by
        // reference identity so each unique Texture pays the encode once per Export call. Cleared
        // when this method returns so we don't leak references across exports.
        var encodeCache = new ConditionalWeakTable<Texture, byte[]>();
        Dictionary<string, MaterialBuilder> materialBuilders;
        using (PerfTimer.Measure("gltf.export", "BuildMaterials"))
        {
            materialBuilders = BuildMaterials(model, encodeCache, bakeUvTransforms);
        }

        // When BakeUvTransforms is set, the consumer can't honor KHR_texture_transform (e.g. the
        // in-app SharpEngine preview), so we fold each material's BakedUv0Scale into the mesh's UVs
        // at write time instead. ApplyUvScale in BuildMaterial is skipped in this mode to avoid
        // double-applying the transform.
        var inlineUvScales = bakeUvTransforms
            ? model.Materials
                .Where(m => m.BakedUv0Scale is not null)
                .ToDictionary(m => m.Name, m => m.BakedUv0Scale!.Value, StringComparer.OrdinalIgnoreCase)
            : null;

        var scene = new SceneBuilder(model.Name);
        var hasSkeleton = model.Skeleton is { Bones.Count: > 0 };
        var isSkinned = CanExportSkin(model);
        var jointNodes = hasSkeleton ? BuildJointNodes(model.Skeleton!) : null;

        // Our importers normalize source data to Z-up, which is also what Source/SMD needs, so
        // MDLExporter writes positions as-is. glTF, however, mandates Y-up; without a conversion
        // the produced .glb claims Y-up while actually holding Z-up data, and every viewer
        // (in-app preview, Blender) renders the model tipped onto its face. Wrap every top-level
        // scene node — both rigid meshes and skeleton roots — under one rotated parent so the
        // mesh vertices, joint bind poses, and animation keyframes can stay untouched while the
        // scene-level transform handles the convention change.
        var zUpToYUp = new NodeBuilder("ZUpToYUp")
        {
            LocalTransform = new AffineTransform(Vector3.One, _zUpToYUpRotation, Vector3.Zero),
        };

        Directory.CreateDirectory(outputDirectory);

        using (PerfTimer.Measure("gltf.export", "BuildScene", $"meshes={model.Meshes.Count}"))
        {
            for (var meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++)
            {
                var mesh = model.Meshes[meshIndex];
                var nodeName = string.IsNullOrWhiteSpace(mesh.Name)
                    ? FormattableString.Invariant($"mesh_{meshIndex}")
                    : mesh.Name;
                // The scene node — not the mesh data block — is what Blender uses as the object
                // name on import, so build it explicitly with a name and attach the mesh to it.
                var node = zUpToYUp.CreateNode(nodeName);
                if (isSkinned)
                {
                    var meshBuilder = BuildSkinnedMesh(mesh, meshIndex, materialBuilders, model.Skeleton!.Bones.Count, inlineUvScales);
                    scene.AddSkinnedMesh(meshBuilder, node.WorldMatrix, jointNodes!);
                }
                else
                {
                    var meshBuilder = BuildMesh(mesh, meshIndex, materialBuilders, inlineUvScales);
                    scene.AddRigidMesh(meshBuilder, node);
                }
            }

            // Re-parent each skeleton root under the axis-fixup node so joint world transforms
            // (and therefore skinning) pick up the same Z-up → Y-up rotation as the meshes. Bones
            // with a parent are already wired up by BuildJointNodes.
            if (jointNodes is not null && model.Skeleton is { } skeleton)
            {
                foreach (var bone in skeleton.Bones.Where(bone => bone.ParentIndex < 0))
                {
                    zUpToYUp.AddNode(jointNodes[bone.Index]);
                }

                AddAnimations(model, jointNodes);
            }
        }

        SharpGLTF.Schema2.ModelRoot gltf;
        using (PerfTimer.Measure("gltf.export", "scene.ToGltf2"))
        {
            gltf = scene.ToGltf2();
        }
        ApplyTextureSamplerDefaults(gltf);

        var settings = new WriteSettings
        {
            ImageWriting = binary ? ResourceWriteMode.BufferView : ResourceWriteMode.SatelliteFile,
            MergeBuffers = true
        };

        using (PerfTimer.Measure("gltf.export", binary ? "SaveGLB" : "SaveGLTF", outputPath))
        {
            if (binary)
            {
                gltf.SaveGLB(outputPath, settings);
            }
            else
            {
                gltf.SaveGLTF(outputPath, settings);
            }
        }
    }

    private static void ApplyTextureSamplerDefaults(SharpGLTF.Schema2.ModelRoot gltf)
    {
        if (gltf.LogicalTextures.Count == 0)
        {
            return;
        }

        var sampler = gltf.UseTextureSampler(
            TextureWrapMode.REPEAT,
            TextureWrapMode.REPEAT,
            TextureMipMapFilter.LINEAR_MIPMAP_LINEAR,
            TextureInterpolationFilter.LINEAR);

        foreach (var texture in gltf.LogicalTextures)
        {
            texture.Sampler = sampler;
        }
    }

    private static bool CanExportSkin(Model model)
    {
        return model.Skeleton is { Bones.Count: > 0 } &&
            model.Meshes
                .SelectMany(mesh => mesh.Vertices)
                .Any(vertex => vertex.BoneWeights is { Count: > 0 });
    }

    private static GltfMeshBuilder BuildMesh(
        Mesh mesh,
        int meshIndex,
        Dictionary<string, MaterialBuilder> materialBuilders,
        IReadOnlyDictionary<string, Vector2>? inlineUvScales)
    {
        var meshBuilder = new GltfMeshBuilder(
            string.IsNullOrWhiteSpace(mesh.Name)
                ? FormattableString.Invariant($"mesh_{meshIndex}")
                : mesh.Name);

        foreach (var submesh in mesh.Submeshes)
        {
            var materialBuilder = ResolveMaterial(submesh.MaterialName, materialBuilders);
            var primitive = meshBuilder.UsePrimitive(materialBuilder);
            var uvScale = LookupInlineUvScale(submesh.MaterialName, inlineUvScales);

            foreach (var triangle in submesh.Triangles)
            {
                primitive.AddTriangle(
                    BuildVertex(mesh.Vertices[triangle.A], uvScale),
                    BuildVertex(mesh.Vertices[triangle.B], uvScale),
                    BuildVertex(mesh.Vertices[triangle.C], uvScale));
            }
        }

        return meshBuilder;
    }

    private static GltfSkinnedMeshBuilder BuildSkinnedMesh(
        Mesh mesh,
        int meshIndex,
        Dictionary<string, MaterialBuilder> materialBuilders,
        int boneCount,
        IReadOnlyDictionary<string, Vector2>? inlineUvScales)
    {
        var meshBuilder = new GltfSkinnedMeshBuilder(
            string.IsNullOrWhiteSpace(mesh.Name)
                ? FormattableString.Invariant($"mesh_{meshIndex}")
                : mesh.Name);

        foreach (var submesh in mesh.Submeshes)
        {
            var materialBuilder = ResolveMaterial(submesh.MaterialName, materialBuilders);
            var primitive = meshBuilder.UsePrimitive(materialBuilder);
            var uvScale = LookupInlineUvScale(submesh.MaterialName, inlineUvScales);

            foreach (var triangle in submesh.Triangles)
            {
                primitive.AddTriangle(
                    BuildSkinnedVertex(mesh.Vertices[triangle.A], boneCount, uvScale),
                    BuildSkinnedVertex(mesh.Vertices[triangle.B], boneCount, uvScale),
                    BuildSkinnedVertex(mesh.Vertices[triangle.C], boneCount, uvScale));
            }
        }

        return meshBuilder;
    }

    private static Vector2 LookupInlineUvScale(string? materialName, IReadOnlyDictionary<string, Vector2>? inlineUvScales)
    {
        if (inlineUvScales is null || string.IsNullOrWhiteSpace(materialName))
        {
            return Vector2.One;
        }

        return inlineUvScales.TryGetValue(materialName, out var scale) ? scale : Vector2.One;
    }

    private static GltfVertexBuilder BuildVertex(Vertex vertex, Vector2 uvScale)
    {
        var (geometry, material) = BuildVertexParts(vertex, uvScale);
        return new GltfVertexBuilder(in geometry, in material);
    }

    private static GltfSkinnedVertexBuilder BuildSkinnedVertex(Vertex vertex, int boneCount, Vector2 uvScale)
    {
        var (geometry, material) = BuildVertexParts(vertex, uvScale);
        var joints = new VertexJoints4(NormalizeWeights(vertex.BoneWeights, boneCount));
        return new GltfSkinnedVertexBuilder(in geometry, in material, in joints);
    }

    private static (VertexPositionNormal Geometry, VertexTexture1 Material) BuildVertexParts(Vertex vertex, Vector2 uvScale)
    {
        var normal = vertex.Normal;
        if (normal.LengthSquared() <= 0.000001f)
        {
            normal = Vector3.UnitZ;
        }
        else
        {
            normal = Vector3.Normalize(normal);
        }

        var geometry = new VertexPositionNormal(vertex.Position.X, vertex.Position.Y, vertex.Position.Z, normal.X, normal.Y, normal.Z);
        var material = new VertexTexture1(new Vector2(vertex.TextureCoordinate.X * uvScale.X, (1.0f - vertex.TextureCoordinate.Y) * uvScale.Y));
        return (geometry, material);
    }

    private static (int JointIndex, float Weight)[] NormalizeWeights(IReadOnlyList<VertexBoneWeight>? weights, int boneCount)
    {
        var normalizedWeights = weights?
            .Where(weight => weight.BoneIndex >= 0 && weight.BoneIndex < boneCount && weight.Weight > 0.0f)
            .GroupBy(weight => weight.BoneIndex)
            .Select(group => (JointIndex: group.Key, Weight: group.Sum(weight => weight.Weight)))
            .OrderByDescending(weight => weight.Weight)
            .Take(4)
            .ToArray();

        if (normalizedWeights is not { Length: > 0 })
        {
            return [(0, 1.0f)];
        }

        var totalWeight = normalizedWeights.Sum(weight => weight.Weight);
        if (totalWeight <= 0.000001f)
        {
            return [(0, 1.0f)];
        }

        for (var index = 0; index < normalizedWeights.Length; index++)
        {
            normalizedWeights[index] = (
                normalizedWeights[index].JointIndex,
                normalizedWeights[index].Weight / totalWeight);
        }

        return normalizedWeights;
    }

    private static NodeBuilder[] BuildJointNodes(Skeleton skeleton)
    {
        var nodes = skeleton.Bones
            .Select(bone => new NodeBuilder(bone.Name))
            .ToArray();

        foreach (var bone in skeleton.Bones)
        {
            nodes[bone.Index].LocalTransform = ToAffineTransform(bone.LocalBindPose);
        }

        foreach (var bone in skeleton.Bones)
        {
            if (bone.ParentIndex >= 0 && bone.ParentIndex < nodes.Length)
            {
                nodes[bone.ParentIndex].AddNode(nodes[bone.Index]);
            }
        }

        return nodes;
    }

    private static AffineTransform ToAffineTransform(Transform transform)
    {
        return new AffineTransform(transform.Scale, transform.Rotation, transform.Translation);
    }

    private static void AddAnimations(Model model, NodeBuilder[] jointNodes)
    {
        if (model.Animations is not { Count: > 0 })
        {
            return;
        }

        foreach (var clip in model.Animations)
        {
            foreach (var track in clip.Tracks.OfType<BoneTransformTrack>())
            {
                if (track.BoneIndex < 0 || track.BoneIndex >= jointNodes.Length || track.Keyframes.Count == 0)
                {
                    continue;
                }

                var node = jointNodes[track.BoneIndex];
                node.WithLocalTranslation(
                    clip.Name,
                    track.Keyframes.ToDictionary(keyframe => keyframe.TimeSeconds, keyframe => keyframe.Transform.Translation));
                node.WithLocalRotation(
                    clip.Name,
                    track.Keyframes.ToDictionary(keyframe => keyframe.TimeSeconds, keyframe => keyframe.Transform.Rotation));

                if (HasNonIdentityScale(track.Keyframes))
                {
                    node.WithLocalScale(
                        clip.Name,
                        track.Keyframes.ToDictionary(keyframe => keyframe.TimeSeconds, keyframe => keyframe.Transform.Scale));
                }
            }
        }
    }

    private static bool HasNonIdentityScale(IReadOnlyList<TransformKeyframe> keyframes)
    {
        return keyframes.Any(keyframe => Vector3.DistanceSquared(keyframe.Transform.Scale, Vector3.One) > 0.000001f);
    }

    private static Dictionary<string, MaterialBuilder> BuildMaterials(
        Model model,
        ConditionalWeakTable<Texture, byte[]> encodeCache,
        bool bakeUvTransforms)
    {
        // Derived-texture caches: WithOpenGlNormalMap / ToGltfMetallicRoughness / ToSpecularFactorMask
        // each produce a new Texture instance per call (Clone + pixel walk). When the same source
        // texture is referenced by N materials, the naive path runs that work N times and the
        // encodeCache can't dedup the encoded PNGs either (different Texture identity per call).
        // Caching by source texture identity collapses each derived variant to one Clone+walk per
        // unique source, which then memoizes through encodeCache on subsequent material lookups.
        // ConditionalWeakTable.GetValue is documented as thread-safe — multiple parallel materials
        // racing for the same source texture all see a single derivation.
        var normalGlCache = new ConditionalWeakTable<Texture, Texture>();
        var metallicRoughnessCache = new ConditionalWeakTable<Texture, Texture>();
        var specularFactorCache = new ConditionalWeakTable<Texture, Texture>();

        // Parallelize across materials: each BuildMaterial constructs an independent MaterialBuilder
        // and only reads the shared texture/derived caches, so there's no write-write contention.
        // The 4.5s sequential cost on a 14-material Fortnite scene collapses to roughly the slowest
        // single material (~800 ms) once the work is spread across cores.
        var concurrent = new ConcurrentDictionary<string, MaterialBuilder>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(model.Materials, material =>
        {
            var builder = BuildMaterial(
                material,
                encodeCache,
                normalGlCache,
                metallicRoughnessCache,
                specularFactorCache,
                bakeUvTransforms);
            concurrent[material.Name] = builder;
        });

        var materialBuilders = new Dictionary<string, MaterialBuilder>(concurrent, StringComparer.OrdinalIgnoreCase);

        if (!materialBuilders.ContainsKey("default"))
        {
            materialBuilders["default"] = BuildDefaultMaterial("default");
        }

        return materialBuilders;
    }

    // Gated behind GMCONVERTER_GLTF_DEBUG_DUMP=1. The dump re-encodes every material's textures to
    // PNG and SHA-hashes them, which dominates Export time on multi-part Fortnite scenes — a
    // full-resolution texture re-encode is 50-200 ms each, and BuildMaterial gets called per part.
    // The diagnostic is only useful when chasing texture corruption between PSKImporter and the
    // .glb writer; off by default.
    private static readonly bool _emitDebugDump =
        Environment.GetEnvironmentVariable("GMCONVERTER_GLTF_DEBUG_DUMP") is { Length: > 0 } flag &&
        !flag.Equals("0", StringComparison.Ordinal) &&
        !flag.Equals("false", StringComparison.OrdinalIgnoreCase);

    private static byte[] EncodeOnce(Texture texture, ConditionalWeakTable<Texture, byte[]> encodeCache)
    {
        if (encodeCache.TryGetValue(texture, out var cached))
        {
            return cached;
        }
        var bytes = texture.ToPngBytes();
        encodeCache.Add(texture, bytes);
        return bytes;
    }

    private static MaterialBuilder BuildMaterial(
        Material material,
        ConditionalWeakTable<Texture, byte[]> encodeCache,
        ConditionalWeakTable<Texture, Texture> normalGlCache,
        ConditionalWeakTable<Texture, Texture> metallicRoughnessCache,
        ConditionalWeakTable<Texture, Texture> specularFactorCache,
        bool bakeUvTransforms)
    {
        if (_emitDebugDump)
        {
            WriteDebugDump(material);
        }

        var builder = BuildDefaultMaterial(material.Name);

        // KHR_texture_transform scale, set when MultiLayerBaker emitted a tile-extended texture
        // and we need to remap the mesh's tiled UV0 into the texture's [0,1] sample range.
        // When bakeUvTransforms is set the scale is folded into the mesh's UVs in BuildVertexParts,
        // so we suppress the extension write here to avoid double-applying it.
        var uvScale = bakeUvTransforms ? null : material.BakedUv0Scale;

        if (material.DiffuseTexture is not null)
        {
            var image = ImageBuilder.From(new MemoryImage(EncodeOnce(material.DiffuseTexture, encodeCache)), material.DiffuseTexture.Name);
            builder.WithBaseColor(image, Vector4.One);
            ApplyUvScale(builder.UseChannel(KnownChannel.BaseColor), uvScale);
        }

        if (material.NormalTexture is not null)
        {
            var normalTexture = material.NormalTextureConvention == MaterialNormalTextureConvention.DirectX
                ? normalGlCache.GetValue(material.NormalTexture, static source => source.WithOpenGlNormalMap())
                : material.NormalTexture;
            var image = ImageBuilder.From(new MemoryImage(EncodeOnce(normalTexture, encodeCache)), normalTexture.Name);
            builder.WithNormal(image, 1.0f);
            ApplyUvScale(builder.UseChannel(KnownChannel.Normal), uvScale);
        }

        if (material.SpecularTexture is not null &&
            material.SpecularTexturePacking == MaterialSpecularTexturePacking.SpecularMetallicRoughness)
        {
            var metallicRoughnessTexture = metallicRoughnessCache.GetValue(
                material.SpecularTexture,
                static source => source.ToGltfMetallicRoughness());
            var metallicRoughnessImage = ImageBuilder.From(
                new MemoryImage(EncodeOnce(metallicRoughnessTexture, encodeCache)),
                metallicRoughnessTexture.Name);
            // BuildDefaultMaterial sets factor 0/1 for the matte-dielectric default. We must
            // override both to 1.0 here so glTF multiplies texture channels by 1 instead of 0 —
            // otherwise the metallic channel is nullified and every Fortnite metal surface renders
            // as smooth plastic (= extremely shiny).
            builder.WithMetallicRoughness(metallicRoughnessImage, metallic: 1.0f, roughness: 1.0f);
            ApplyUvScale(builder.UseChannel(KnownChannel.MetallicRoughness), uvScale);

            // KHR_materials_specular: per-pixel modulator (Fortnite's SpecularMasks.R, placed in
            // texture.A by ToSpecularFactorMask) scaled by Material.SpecularFactor. The scalar is
            // set by the importer based on the source format's specular convention — formats whose
            // values already line up with glTF's dielectric F0=0.04 default leave it at 1.0; ones
            // that need damping (e.g. Fortnite, where the in-game look is far more matte than the
            // raw values imply) lower it. Skip the extension entirely when SpecularFactor=1.0 so
            // we don't bloat the glTF with a no-op extension write.
            if (Math.Abs(material.SpecularFactor - 1.0f) > 0.0001f)
            {
                var specularFactorTexture = specularFactorCache.GetValue(
                    material.SpecularTexture,
                    static source => source.ToSpecularFactorMask());
                var specularFactorImage = ImageBuilder.From(
                    new MemoryImage(EncodeOnce(specularFactorTexture, encodeCache)),
                    specularFactorTexture.Name);
                builder.UseChannel(KnownChannel.SpecularFactor)
                    .UseTexture()
                    .WithPrimaryImage(specularFactorImage);
                builder.UseChannel(KnownChannel.SpecularFactor).Parameters["SpecularFactor"] = material.SpecularFactor;
                ApplyUvScale(builder.UseChannel(KnownChannel.SpecularFactor), uvScale);
            }
        }

        if (material.EmissiveTexture is not null)
        {
            var image = ImageBuilder.From(new MemoryImage(EncodeOnce(material.EmissiveTexture, encodeCache)), material.EmissiveTexture.Name);
            builder.WithEmissive(image, Vector3.One);
            ApplyUvScale(builder.UseChannel(KnownChannel.Emissive), uvScale);
        }

        // Core glTF has no additive mode, so additive materials keep the alpha-based fallback.
        if (material.BlendMode == MaterialBlendMode.AlphaTest && material.HasAlpha)
        {
            builder.WithAlpha(AlphaMode.MASK, material.AlphaCutoff);
        }
        else if (material.HasAlpha)
        {
            builder.WithAlpha(AlphaMode.BLEND, 0.5f);
        }

        return builder;
    }

    private static void WriteDebugDump(Material material)
    {
        try
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var logRoot = Path.Join(Path.GetTempPath(), "GMConverter.UI", "GltfBuild");
            Directory.CreateDirectory(logRoot);
            // Path.GetFileName strips any directory components if SanitizeFileName ever lets one
            // through; combined with Path.Join (no rooted-second-arg reset) the log path can't
            // escape logRoot even on pathological material names.
            var logFileName = Path.GetFileName($"{NameHelpers.SanitizeFileName(material.Name)}.gltfbuild.log");
            var logPath = Path.Join(logRoot, logFileName);
            var sb = new System.Text.StringBuilder();
            sb.Append("materialName=").AppendLine(material.Name);
            sb.Append("diffuse=").AppendLine(material.DiffuseTexture?.Name ?? "<null>");
            sb.Append("normal=").AppendLine(material.NormalTexture?.Name ?? "<null>");
            sb.Append("specular=").AppendLine(material.SpecularTexture?.Name ?? "<null>");
            sb.Append("emissive=").AppendLine(material.EmissiveTexture?.Name ?? "<null>");
            sb.Append("normalTextureConvention=").AppendLine(material.NormalTextureConvention.ToString());
            sb.Append("specularTexturePacking=").AppendLine(material.SpecularTexturePacking.ToString());
            sb.Append("hasAlpha=").AppendLine(material.HasAlpha.ToString(inv));
            sb.Append("instance.diffuse.HashCode=").AppendLine(
                (material.DiffuseTexture?.GetHashCode() ?? 0).ToString(inv));
            sb.Append("instance.normal.HashCode=").AppendLine(
                (material.NormalTexture?.GetHashCode() ?? 0).ToString(inv));
            sb.Append("instance.specular.HashCode=").AppendLine(
                (material.SpecularTexture?.GetHashCode() ?? 0).ToString(inv));
            string Sha(byte[] b)
                => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b))[..16];
            if (material.DiffuseTexture is not null)
            {
                var bytes = material.DiffuseTexture.ToPngBytes();
                sb.Append("bytes.diffuse.len=").Append(bytes.Length.ToString(inv))
                    .Append(" sha=").AppendLine(Sha(bytes));
            }
            if (material.NormalTexture is not null)
            {
                var tex = material.NormalTextureConvention == MaterialNormalTextureConvention.DirectX
                    ? material.NormalTexture.WithOpenGlNormalMap()
                    : material.NormalTexture;
                var bytes = tex.ToPngBytes();
                sb.Append("bytes.normal.outName=").AppendLine(tex.Name);
                sb.Append("bytes.normal.len=").Append(bytes.Length.ToString(inv))
                    .Append(" sha=").AppendLine(Sha(bytes));
                sb.Append("bytes.normal.dim=").Append(tex.DebugDimensions).AppendLine();
                sb.Append("source.normal.dim=").Append(material.NormalTexture.DebugDimensions).AppendLine();
            }
            if (material.SpecularTexture is not null)
            {
                var bytes = material.SpecularTexture.ToPngBytes();
                sb.Append("bytes.specularRaw.len=").Append(bytes.Length.ToString(inv))
                    .Append(" sha=").AppendLine(Sha(bytes));
                if (material.SpecularTexturePacking == MaterialSpecularTexturePacking.SpecularMetallicRoughness)
                {
                    var mr = material.SpecularTexture.ToGltfMetallicRoughness().ToPngBytes();
                    sb.Append("bytes.metallicRoughness.len=").Append(mr.Length.ToString(inv))
                        .Append(" sha=").AppendLine(Sha(mr));
                    var sf = material.SpecularTexture.ToSpecularFactorMask().ToPngBytes();
                    sb.Append("bytes.specularFactor.len=").Append(sf.Length.ToString(inv))
                        .Append(" sha=").AppendLine(Sha(sf));
                }
            }
            File.WriteAllText(logPath, sb.ToString());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Diagnostics must not break export. Anything short of process-fatal (OOM)
            // is swallowed — the dump runs ToPngBytes / SHA / file IO and each step has
            // several plausible failure modes, none of which should fail the export.
        }
    }

    // Applies KHR_texture_transform's `scale` to the given channel's texture sampler. When the
    // baker emits a tile-extended texture (e.g. 3*W wide because the mesh's UV0 spans [0,3]), we
    // multiply mesh UVs by (1/3, 1) so they sample the correct tile region of the baked texture.
    // Without this, the mesh's tiled UVs would wrap via the default REPEAT sampler and read from
    // the wrong tile of the bake.
    private static void ApplyUvScale(ChannelBuilder channel, Vector2? scale)
    {
        if (!scale.HasValue)
        {
            return;
        }

        var texture = channel.Texture;
        if (texture is null)
        {
            return;
        }

        texture.WithTransform(Vector2.Zero, scale.Value);
    }

    private static MaterialBuilder BuildDefaultMaterial(string name)
    {
        return new MaterialBuilder(name)
            .WithMetallicRoughnessShader()
            .WithMetallicRoughness(0.0f, 1.0f)
            .WithBaseColor(Vector4.One)
            .WithDoubleSide(true);
    }

    private static MaterialBuilder ResolveMaterial(
        string? materialName,
        Dictionary<string, MaterialBuilder> materialBuilders)
    {
        if (!string.IsNullOrWhiteSpace(materialName) &&
            materialBuilders.TryGetValue(materialName, out var materialBuilder))
        {
            return materialBuilder;
        }

        return materialBuilders["default"];
    }
}

