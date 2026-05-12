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
        var builder = BuildDefaultMaterial(material.Name);

        if (material.DiffuseTexture is not null)
        {
            var image = ImageBuilder.From(new MemoryImage(material.DiffuseTexture.ToPngBytes()), material.DiffuseTexture.Name);
            builder.WithBaseColor(image, Vector4.One);
        }

        if (material.NormalTexture is not null)
        {
            var image = ImageBuilder.From(new MemoryImage(material.NormalTexture.ToPngBytes()), material.NormalTexture.Name);
            builder.WithNormal(image, 1.0f);
        }

        if (material.HasAlpha)
        {
            builder.WithAlpha(AlphaMode.BLEND, 0.5f);
        }

        return builder;
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
