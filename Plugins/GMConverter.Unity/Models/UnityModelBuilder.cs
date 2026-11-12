using System.Numerics;
using AssetsTools.NET.Extra;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Textures;
using GMConverter.Unity.Animation;
using GMConverter.Unity.Assets;
using GMConverter.Unity.Geometry;
using GMConverter.Unity.Materials;
using GMConverter.Unity.Scenes;

namespace GMConverter.Unity.Models;

/// <summary>
/// Builds SDK models from Unity objects: a game object hierarchy (a prefab or a scene object, with
/// every renderer under it) or a single mesh. Skinned meshes are posed by their bones' current
/// transforms, so the mesh matches the skeleton's bind pose, which is the hierarchy as saved.
/// </summary>
internal sealed class UnityModelBuilder(UnityAssetLibrary library, ITextureFactory textureFactory)
{
    private readonly UnityMaterialBuilder _materials = new(library, textureFactory);

    /// <summary>Textures left out because their format could not be decoded.</summary>
    public IReadOnlyList<string> SkippedTextures => _materials.SkippedTextures;

    /// <summary>Clips left out, with the reason.</summary>
    public IReadOnlyList<string> SkippedAnimations { get; private set; } = [];

    public Model BuildHierarchy(UnityObject rootTransform, bool includeInactive, UnityAnimationRequest animations)
    {
        var hierarchy = UnityHierarchy.Load(library, rootTransform);
        var animators = UnityAnimators.Find(library, hierarchy, animations);
        var toggled = animators.ToggledNodes();
        var renderers = FindRenderers(hierarchy, includeInactive, toggled);

        // Props the clips move or turn on and off need bones of their own for their meshes to
        // follow; those saved switched off start hidden.
        HashSet<UnityTransformNode> drawn = [.. renderers.SelectMany(renderer => renderer.Node.Ancestors().Prepend(renderer.Node))];
        HashSet<UnityTransformNode> toggledProps = [.. toggled.Where(drawn.Contains)];
        UnityTransformNode[] props = [.. animators.MovedNodes().Where(drawn.Contains).Union(toggledProps)];
        var skeleton = renderers.Any(renderer => renderer.IsSkinned) || props.Length > 0
            ? BuildSkeleton(hierarchy, renderers, props, toggledProps)
            : null;

        List<Mesh> meshes = [];
        foreach (var renderer in renderers)
        {
            var data = UnityMeshReader.Read(library, renderer.Mesh);
            var materials = ReadMaterials(renderer.Renderer);
            meshes.Add(renderer.IsSkinned && data.IsSkinned
                ? BuildSkinnedMesh(renderer, data, materials, skeleton!)
                : BuildRigidMesh(renderer.Node, data, materials, skeleton));
        }

        return new Model(
            hierarchy.Root.Name,
            meshes,
            CollectMaterials(),
            skeleton?.Skeleton,
            skeleton is null ? null : BuildAnimations(animators, skeleton, toggledProps),
            Forward: UnitySpace.Forward);
    }

    private List<AnimationClip>? BuildAnimations(UnityAnimators animators, UnitySkeleton skeleton, IReadOnlySet<UnityTransformNode> toggledProps)
    {
        var animations = animators.Build(skeleton, toggledProps);
        SkippedAnimations = animators.Skipped;
        return animations;
    }

    public Model BuildMesh(UnityObject mesh, UnityObject? materialRenderer)
    {
        var data = UnityMeshReader.Read(library, mesh);
        var materials = materialRenderer is null ? [_materials.Build(null)] : ReadMaterials(materialRenderer);
        var transform = materialRenderer is null ? Matrix4x4.Identity : GetPlacement(materialRenderer);
        var vertices = Enumerable.Range(0, data.VertexCount)
            .Select(index => ConvertVertex(data, index, transform, null))
            .ToArray();

        return new Model(
            mesh.Name,
            [BuildSdkMesh(mesh.Name, data, vertices, materials, mirrored: transform.GetDeterminant() < 0f)],
            CollectMaterials(),
            Forward: UnitySpace.Forward);
    }

    // A bare mesh is often authored in another tool's units and axes (a Blender mesh is Z up in
    // centimetres) and corrected by the object drawing it. Keep that object's own rotation and the
    // scale of its whole parent chain, but not where or which way the scene placed it.
    private Matrix4x4 GetPlacement(UnityObject renderer)
    {
        if (library.Resolve(renderer.File, renderer["m_GameObject"]) is not { } gameObject ||
            UnityFields.Items(gameObject["m_Component"])
                .Select(component => library.Resolve(gameObject.File, component["component"]))
                .FirstOrDefault(component => component?.ClassId == AssetClassID.Transform) is not { } transform)
        {
            return Matrix4x4.Identity;
        }

        var rotation = Quaternion.Normalize(UnitySpace.ReadQuaternion(transform["m_LocalRotation"]));
        var scale = Vector3.One;
        for (UnityObject? current = transform; current is not null; current = library.Resolve(current.File, current["m_Father"]))
        {
            scale *= UnitySpace.ReadVector3(current["m_LocalScale"]);
        }

        return UnitySpace.Compose(Vector3.Zero, rotation, scale);
    }

    private List<UnityRenderer> FindRenderers(UnityHierarchy hierarchy, bool includeInactive, IReadOnlySet<UnityTransformNode> toggled)
    {
        List<UnityRenderer> renderers = [];
        foreach (var node in hierarchy.Nodes.Where(node => includeInactive || node.IsActive || IsShownByClips(node, toggled)))
        {
            foreach (var renderer in hierarchy.GetComponents(node, AssetClassID.SkinnedMeshRenderer))
            {
                if (IsEnabled(renderer, includeInactive) && library.Resolve(renderer.File, renderer["m_Mesh"]) is { } mesh)
                {
                    var bones = UnityFields.Items(renderer["m_Bones"])
                        .Select(bone => library.Resolve(renderer.File, bone) is { } transform ? hierarchy.Find(transform.Key) : null)
                        .ToArray();
                    renderers.Add(new UnityRenderer(node, renderer, mesh, bones));
                }
            }

            foreach (var renderer in hierarchy.GetComponents(node, AssetClassID.MeshRenderer))
            {
                if (IsEnabled(renderer, includeInactive) &&
                    hierarchy.GetComponents(node, AssetClassID.MeshFilter).FirstOrDefault() is { } filter &&
                    library.Resolve(filter.File, filter["m_Mesh"]) is { } mesh)
                {
                    renderers.Add(new UnityRenderer(node, renderer, mesh, []));
                }
            }
        }

        return renderers;
    }

    // Whether every switched-off game object above the node is one a clip switches on.
    private static bool IsShownByClips(UnityTransformNode node, IReadOnlySet<UnityTransformNode> toggled)
    {
        return node.Ancestors().Prepend(node).All(current =>
            current.GameObject?["m_IsActive"].AsBool != false || toggled.Contains(current));
    }

    private static bool IsEnabled(UnityObject renderer, bool includeInactive)
    {
        return includeInactive || renderer["m_Enabled"].AsBool;
    }

    // Keeps every bone a skinned renderer uses and every animated prop, plus their ancestors, so each
    // bone's parent is its parent in the hierarchy and the root is always present for rigid parts
    // to fall back on.
    private static UnitySkeleton BuildSkeleton(
        UnityHierarchy hierarchy,
        IReadOnlyList<UnityRenderer> renderers,
        IEnumerable<UnityTransformNode> props,
        HashSet<UnityTransformNode> toggledProps)
    {
        HashSet<UnityTransformNode> used = [hierarchy.Root];
        foreach (var bone in renderers.SelectMany(renderer => renderer.Bones).OfType<UnityTransformNode>().Concat(props))
        {
            if (used.Add(bone))
            {
                used.UnionWith(bone.Ancestors());
            }
        }

        var ordered = hierarchy.Nodes.Where(used.Contains).ToArray();
        var indices = ordered.Select((node, index) => (node, index)).ToDictionary(pair => pair.node, pair => pair.index);
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        var bones = ordered.Select((node, index) => new Bone(
                index,
                UniqueName(names, node.Name),
                node.Parent is { } parent ? indices[parent] : -1,
                new Transform(UnitySpace.Position(node.LocalPosition), UnitySpace.Rotation(node.LocalRotation), UnitySpace.Scale(node.LocalScale)),
                HiddenAtRest: toggledProps.Contains(node) && !node.IsActive))
            .ToArray();

        return new UnitySkeleton(new Skeleton(bones), indices);
    }

    private static Mesh BuildSkinnedMesh(UnityRenderer renderer, UnityMeshData data, IReadOnlyList<UnityMaterial> materials, UnitySkeleton skeleton)
    {
        var boneCount = renderer.Bones.Count;
        var skinMatrices = new Matrix4x4[boneCount];
        var boneIndices = new int[boneCount];
        for (var bone = 0; bone < boneCount; bone++)
        {
            var node = renderer.Bones[bone] ?? renderer.Node;
            var bindPose = bone < data.BindPoses.Length ? data.BindPoses[bone] : Matrix4x4.Identity;
            skinMatrices[bone] = bindPose * node.World;
            boneIndices[bone] = skeleton.IndexOf(node);
        }

        var vertices = new Vertex[data.VertexCount];
        for (var vertex = 0; vertex < data.VertexCount; vertex++)
        {
            var skin = new Matrix4x4();
            var weights = new List<VertexBoneWeight>(UnityMeshData.InfluencesPerVertex);
            var total = 0f;
            for (var slot = 0; slot < UnityMeshData.InfluencesPerVertex; slot++)
            {
                var influence = vertex * UnityMeshData.InfluencesPerVertex + slot;
                var bone = data.BoneIndices![influence];
                var weight = data.BoneWeights![influence];
                if (weight <= 0f || bone < 0 || bone >= boneCount)
                {
                    continue;
                }

                skin += skinMatrices[bone] * weight;
                total += weight;
                weights.Add(new VertexBoneWeight(boneIndices[bone], weight));
            }

            if (total <= 0f)
            {
                vertices[vertex] = ConvertVertex(data, vertex, renderer.Node.World, [new VertexBoneWeight(skeleton.IndexOf(renderer.Node), 1f)]);
                continue;
            }

            vertices[vertex] = ConvertVertex(data, vertex, skin * (1f / total), MergeWeights(weights, total));
        }

        return BuildSdkMesh(renderer.Node.Name, data, vertices, materials, mirrored: false);
    }

    private static Mesh BuildRigidMesh(UnityTransformNode node, UnityMeshData data, IReadOnlyList<UnityMaterial> materials, UnitySkeleton? skeleton)
    {
        IReadOnlyList<VertexBoneWeight>? weights = skeleton is null ? null : [new VertexBoneWeight(skeleton.IndexOf(node), 1f)];
        var vertices = Enumerable.Range(0, data.VertexCount)
            .Select(index => ConvertVertex(data, index, node.World, weights))
            .ToArray();
        return BuildSdkMesh(node.Name, data, vertices, materials, mirrored: node.World.GetDeterminant() < 0f);
    }

    private static Vertex ConvertVertex(UnityMeshData data, int index, Matrix4x4 transform, IReadOnlyList<VertexBoneWeight>? weights)
    {
        var position = Vector3.Transform(data.Positions[index], transform);
        var normal = data.Normals is { } normals ? Vector3.TransformNormal(normals[index], transform) : Vector3.UnitY;
        normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
        var uv = data.TextureCoordinates is { } uvs ? uvs[index] : Vector2.Zero;

        return new Vertex(
            UnitySpace.Position(position),
            UnitySpace.Direction(normal),
            UnitySpace.TextureCoordinate(uv),
            weights);
    }

    // Each submesh gets its own copy of the vertices it uses when its material tiles or offsets
    // the texture, since the SDK keeps texture coordinates on the vertices.
    private static Mesh BuildSdkMesh(string name, UnityMeshData data, Vertex[] vertices, IReadOnlyList<UnityMaterial> materials, bool mirrored)
    {
        List<Vertex> output = [.. vertices];
        List<Submesh> submeshes = [];

        // Unity's left-handed space mirrors into the SDK's, which reverses the winding once; a
        // negatively scaled object reverses it back.
        var reverse = !mirrored;

        for (var index = 0; index < data.Submeshes.Count; index++)
        {
            var material = materials[Math.Min(index, materials.Count - 1)];
            var indices = data.Submeshes[index];
            Dictionary<int, int>? remap = material.HasUvTransform ? [] : null;
            List<Triangle> triangles = new(indices.Length / 3);

            for (var corner = 0; corner + 2 < indices.Length; corner += 3)
            {
                var a = Remap(indices[corner]);
                var b = Remap(indices[corner + 1]);
                var c = Remap(indices[corner + 2]);
                triangles.Add(reverse ? new Triangle(a, c, b) : new Triangle(a, b, c));
            }

            submeshes.Add(new Submesh(material.Material.Name, triangles));

            int Remap(int vertex)
            {
                if (remap is null)
                {
                    return vertex;
                }

                if (!remap.TryGetValue(vertex, out var copy))
                {
                    var source = vertices[vertex];
                    copy = output.Count;
                    output.Add(source with { TextureCoordinate = material.Apply(source.TextureCoordinate) });
                    remap[vertex] = copy;
                }

                return copy;
            }
        }

        return new Mesh(output, submeshes, name);
    }

    private static List<VertexBoneWeight> MergeWeights(List<VertexBoneWeight> weights, float total)
    {
        return [.. weights
            .GroupBy(weight => weight.BoneIndex)
            .Select(group => new VertexBoneWeight(group.Key, group.Sum(weight => weight.Weight) / total))];
    }

    private List<UnityMaterial> ReadMaterials(UnityObject renderer)
    {
        List<UnityMaterial> materials = [.. UnityFields.Items(renderer["m_Materials"])
            .Select(pointer => _materials.Build(library.Resolve(renderer.File, pointer)))];
        return materials.Count == 0 ? [_materials.Build(null)] : materials;
    }

    private List<SDK.Materials.Material> CollectMaterials()
    {
        return [.. _materials.Converted.Select(material => material.Material)];
    }

    private static string UniqueName(HashSet<string> used, string name)
    {
        var candidate = name;
        for (var suffix = 2; !used.Add(candidate); suffix++)
        {
            candidate = $"{name}_{suffix}";
        }

        return candidate;
    }
}
