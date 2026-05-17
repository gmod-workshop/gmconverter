using System.Numerics;
using GMConverter.Common;
using GMConverter.Geometry;
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

internal sealed class GLTFExporter : IExporter<GLTFExportOptions>
{
    public string OutputFormat => "glb";

    public string OutputName => "glTF";

    public void Export(Model model, string outputDirectory, string baseName, GLTFExportOptions options)
    {
        var safeBaseName = NameHelpers.SanitizeFileName(baseName);
        var extension = options.Binary ? ".glb" : ".gltf";
        var outputPath = Path.Combine(outputDirectory, $"{safeBaseName}{extension}");
        var materialBuilders = BuildMaterials(model);
        var scene = new SceneBuilder(model.Name);
        var hasSkeleton = model.Skeleton is { Bones.Count: > 0 };
        var isSkinned = CanExportSkin(model);
        var jointNodes = hasSkeleton ? BuildJointNodes(model.Skeleton!) : null;

        Directory.CreateDirectory(outputDirectory);

        for (var meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++)
        {
            var mesh = model.Meshes[meshIndex];
            if (isSkinned)
            {
                var meshBuilder = BuildSkinnedMesh(mesh, meshIndex, materialBuilders, model.Skeleton!.Bones.Count);
                scene.AddSkinnedMesh(meshBuilder, Matrix4x4.Identity, jointNodes!);
            }
            else
            {
                var meshBuilder = BuildMesh(mesh, meshIndex, materialBuilders);
                scene.AddRigidMesh(meshBuilder, new AffineTransform(Matrix4x4.Identity));
            }
        }

        if (jointNodes is not null)
        {
            AddAnimations(model, jointNodes);
        }

        var gltf = scene.ToGltf2();
        ApplyTextureSamplerDefaults(gltf);

        var settings = new WriteSettings
        {
            ImageWriting = options.Binary ? ResourceWriteMode.BufferView : ResourceWriteMode.SatelliteFile,
            MergeBuffers = true
        };

        if (options.Binary)
        {
            gltf.SaveGLB(outputPath, settings);
        }
        else
        {
            gltf.SaveGLTF(outputPath, settings);
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
        Dictionary<string, MaterialBuilder> materialBuilders)
    {
        var meshBuilder = new GltfMeshBuilder(FormattableString.Invariant($"mesh_{meshIndex}"));

        foreach (var submesh in mesh.Submeshes)
        {
            var materialBuilder = ResolveMaterial(submesh.MaterialName, materialBuilders);
            var primitive = meshBuilder.UsePrimitive(materialBuilder);

            foreach (var triangle in submesh.Triangles)
            {
                primitive.AddTriangle(
                    BuildVertex(mesh.Vertices[triangle.A]),
                    BuildVertex(mesh.Vertices[triangle.B]),
                    BuildVertex(mesh.Vertices[triangle.C]));
            }
        }

        return meshBuilder;
    }

    private static GltfSkinnedMeshBuilder BuildSkinnedMesh(
        Mesh mesh,
        int meshIndex,
        Dictionary<string, MaterialBuilder> materialBuilders,
        int boneCount)
    {
        var meshBuilder = new GltfSkinnedMeshBuilder(FormattableString.Invariant($"mesh_{meshIndex}"));

        foreach (var submesh in mesh.Submeshes)
        {
            var materialBuilder = ResolveMaterial(submesh.MaterialName, materialBuilders);
            var primitive = meshBuilder.UsePrimitive(materialBuilder);

            foreach (var triangle in submesh.Triangles)
            {
                primitive.AddTriangle(
                    BuildSkinnedVertex(mesh.Vertices[triangle.A], boneCount),
                    BuildSkinnedVertex(mesh.Vertices[triangle.B], boneCount),
                    BuildSkinnedVertex(mesh.Vertices[triangle.C], boneCount));
            }
        }

        return meshBuilder;
    }

    private static GltfVertexBuilder BuildVertex(Vertex vertex)
    {
        var (geometry, material) = BuildVertexParts(vertex);
        return new GltfVertexBuilder(in geometry, in material);
    }

    private static GltfSkinnedVertexBuilder BuildSkinnedVertex(Vertex vertex, int boneCount)
    {
        var (geometry, material) = BuildVertexParts(vertex);
        var joints = new VertexJoints4(NormalizeWeights(vertex.BoneWeights, boneCount));
        return new GltfSkinnedVertexBuilder(in geometry, in material, in joints);
    }

    private static (VertexPositionNormal Geometry, VertexTexture1 Material) BuildVertexParts(Vertex vertex)
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
        var material = new VertexTexture1(new Vector2(vertex.TextureCoordinate.X, 1.0f - vertex.TextureCoordinate.Y));
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

    private static Dictionary<string, MaterialBuilder> BuildMaterials(Model model)
    {
        Dictionary<string, MaterialBuilder> materialBuilders = new(StringComparer.OrdinalIgnoreCase);

        foreach (var material in model.Materials)
        {
            materialBuilders[material.Name] = BuildMaterial(material);
        }

        if (!materialBuilders.ContainsKey("default"))
        {
            materialBuilders["default"] = BuildDefaultMaterial("default");
        }

        return materialBuilders;
    }

    private static MaterialBuilder BuildMaterial(Material material)
    {
        // Diagnostic: dump what GLTFExporter sees on the incoming Material object. Cross-reference
        // with the corresponding .resolve.log to detect whether textures are getting corrupted in
        // SharpGLTF's image deduplication or somewhere between PSKImporter and the .glb writer.
        try
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var logRoot = Path.Combine(Path.GetTempPath(), "GMConverter.UI", "GltfBuild");
            Directory.CreateDirectory(logRoot);
            var logPath = Path.Combine(logRoot, $"{NameHelpers.SanitizeFileName(material.Name)}.gltfbuild.log");
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
                if (material.SpecularTexturePacking == MaterialSpecularTexturePacking.UnrealSpecularMasks)
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
        catch
        {
            // diagnostics must not break export
        }

        var builder = BuildDefaultMaterial(material.Name);

        // KHR_texture_transform scale, set when MultiLayerBaker emitted a tile-extended texture
        // and we need to remap the mesh's tiled UV0 into the texture's [0,1] sample range.
        var uvScale = material.BakedUv0Scale;

        if (material.DiffuseTexture is not null)
        {
            var image = ImageBuilder.From(new MemoryImage(material.DiffuseTexture.ToPngBytes()), material.DiffuseTexture.Name);
            builder.WithBaseColor(image, Vector4.One);
            ApplyUvScale(builder.UseChannel(KnownChannel.BaseColor), uvScale);
        }

        if (material.NormalTexture is not null)
        {
            var normalTexture = material.NormalTextureConvention == MaterialNormalTextureConvention.DirectX
                ? material.NormalTexture.WithOpenGlNormalMap()
                : material.NormalTexture;
            var image = ImageBuilder.From(new MemoryImage(normalTexture.ToPngBytes()), normalTexture.Name);
            builder.WithNormal(image, 1.0f);
            ApplyUvScale(builder.UseChannel(KnownChannel.Normal), uvScale);
        }

        if (material.SpecularTexture is not null &&
            material.SpecularTexturePacking == MaterialSpecularTexturePacking.UnrealSpecularMasks)
        {
            var metallicRoughnessTexture = material.SpecularTexture.ToGltfMetallicRoughness();
            var metallicRoughnessImage = ImageBuilder.From(
                new MemoryImage(metallicRoughnessTexture.ToPngBytes()),
                metallicRoughnessTexture.Name);
            // BuildDefaultMaterial sets factor 0/1 for the matte-dielectric default. We must
            // override both to 1.0 here so glTF multiplies texture channels by 1 instead of 0 —
            // otherwise the metallic channel is nullified and every Fortnite metal surface renders
            // as smooth plastic (= extremely shiny).
            builder.WithMetallicRoughness(metallicRoughnessImage, metallic: 1.0f, roughness: 1.0f);
            ApplyUvScale(builder.UseChannel(KnownChannel.MetallicRoughness), uvScale);

            // KHR_materials_specular — feed the Fortnite SpecularMasks R channel into glTF's
            // specularFactor texture so renderers (Blender, three.js, model-viewer) use the same
            // dielectric reflectance value FortnitePorting routes into Principled BSDF's
            // "Specular IOR Level". Without this, non-metallic surfaces use the glTF default
            // (0.5 = IOR 1.5) and look uniformly more reflective than Fortnite intends.
            var specularFactorTexture = material.SpecularTexture.ToSpecularFactorMask();
            var specularFactorImage = ImageBuilder.From(
                new MemoryImage(specularFactorTexture.ToPngBytes()),
                specularFactorTexture.Name);
            builder.UseChannel(KnownChannel.SpecularFactor)
                .UseTexture()
                .WithPrimaryImage(specularFactorImage);
            builder.UseChannel(KnownChannel.SpecularFactor).Parameters["SpecularFactor"] = 1.0f;
            ApplyUvScale(builder.UseChannel(KnownChannel.SpecularFactor), uvScale);
        }

        if (material.EmissiveTexture is not null)
        {
            var image = ImageBuilder.From(new MemoryImage(material.EmissiveTexture.ToPngBytes()), material.EmissiveTexture.Name);
            builder.WithEmissive(image, Vector3.One);
            ApplyUvScale(builder.UseChannel(KnownChannel.Emissive), uvScale);
        }

        if (material.HasAlpha)
        {
            builder.WithAlpha(AlphaMode.BLEND, 0.5f);
        }

        return builder;
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

internal sealed record GLTFExportOptions(bool Binary = true);
