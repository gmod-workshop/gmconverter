using System.Globalization;
using System.Numerics;
using GMConverter.Godot.Geometry;
using GMConverter.Godot.Materials;
using GMConverter.Godot.Packs;
using GMConverter.Godot.Resources;
using GMConverter.Godot.Scenes;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Textures;

namespace GMConverter.Godot.Models;

/// <summary>
/// Builds SDK models from an instantiated Godot scene: every <c>MeshInstance3D</c> under the
/// root. Skinned meshes are posed by their skeleton's saved pose, so the mesh matches the
/// skeleton's bind pose; static meshes are placed where the scene puts them, on a bone of their
/// own when the model has a skeleton.
/// </summary>
internal sealed class GodotModelBuilder(GodotResourceLoader loader, ITextureFactory textureFactory)
{
    private readonly GodotMaterialBuilder _materials = new(loader, textureFactory);
    private readonly Dictionary<GodotSceneNode, GodotSkeletonBones> _skeletons = [];

    /// <summary>Textures left out because they could not be read or decoded.</summary>
    public IReadOnlyList<string> SkippedTextures => _materials.SkippedTextures;

    /// <summary>Meshes left out, such as the primitive shapes Godot generates at run time.</summary>
    public List<string> SkippedMeshes { get; } = [];

    public Model Build(GodotSceneNode root, string name, bool includeHidden)
    {
        var instances = root.Descendants()
            .Where(node => node.Type == "MeshInstance3D" && (includeHidden || node.IsVisible))
            .Select(node => (Node: node, Mesh: ReadMesh(node)))
            .Where(instance => instance.Mesh is not null)
            .ToArray();

        var skinned = instances.Where(instance => instance.Mesh!.Any(surface => surface.IsSkinned) && FindSkeleton(instance.Node) is not null).ToArray();
        var skeleton = skinned.Length > 0 ? BuildSkeleton(root, instances.Select(instance => instance.Node), skinned.Select(instance => FindSkeleton(instance.Node)!)) : null;

        List<Mesh> meshes = [];
        foreach (var (node, surfaces) in instances)
        {
            var skeletonBones = FindSkeleton(node);
            meshes.Add(skeleton is not null && skeletonBones is not null && surfaces!.Any(surface => surface.IsSkinned)
                ? BuildSkinnedMesh(node, surfaces!, skeletonBones, skeleton)
                : BuildRigidMesh(node, surfaces!, skeleton));
        }

        return new Model(
            name,
            meshes,
            [.. _materials.Converted.Select(material => material.Material)],
            skeleton?.Skeleton,
            Forward: GodotSpace.Forward);
    }

    private GodotSurface[]? ReadMesh(GodotSceneNode node)
    {
        var mesh = loader.Get(node["mesh"]);
        if (mesh is null)
        {
            return null;
        }

        if (mesh.Type != "ArrayMesh")
        {
            SkippedMeshes.Add($"{node.Name} ({mesh.Type})");
            return null;
        }

        var surfaces = GodotSurfaceReader.Read(mesh).ToArray();
        return surfaces.Length > 0 ? surfaces : null;
    }

    // The Skeleton3D a mesh instance's `skeleton` path names (its parent by default).
    private GodotSkeletonBones? FindSkeleton(GodotSceneNode node)
    {
        var path = node["skeleton"] is GodotNodePath saved ? saved.Names : [".."];
        if (node.Find(path) is not { Type: "Skeleton3D" } skeletonNode)
        {
            return null;
        }

        if (!_skeletons.TryGetValue(skeletonNode, out var bones))
        {
            bones = new GodotSkeletonBones(skeletonNode);
            _skeletons[skeletonNode] = bones;
        }

        return bones;
    }

    // Keeps every scene node that holds a mesh or a skeleton, with the nodes above it, and every
    // bone of the skeletons used, so each bone's parent is its parent in the scene.
    private GodotSkeleton BuildSkeleton(GodotSceneNode root, IEnumerable<GodotSceneNode> meshNodes, IEnumerable<GodotSkeletonBones> skeletons)
    {
        var usedSkeletons = skeletons.Distinct().ToArray();
        HashSet<GodotSceneNode> usedNodes = [root];
        foreach (var node in meshNodes.Concat(usedSkeletons.Select(bones => bones.Node)))
        {
            usedNodes.Add(node);
            usedNodes.UnionWith(node.Ancestors());
        }

        List<(GodotBoneKey Key, GodotBoneKey? Parent, Matrix4x4 Local, string Name)> ordered = [];
        foreach (var node in root.Descendants().Where(usedNodes.Contains))
        {
            if (AttachedBone(node) is not null)
            {
                continue; // a bone attachment is the bone it follows
            }

            ordered.Add((new GodotBoneKey(node), node.Parent is { } parent ? ParentKey(parent) : null, node.Local, node.Name));
            if (usedSkeletons.FirstOrDefault(bones => bones.Node == node) is { } skeletonBones)
            {
                for (var bone = 0; bone < skeletonBones.Count; bone++)
                {
                    var parentKey = skeletonBones.Parents[bone] >= 0 ? new GodotBoneKey(node, skeletonBones.Parents[bone]) : new GodotBoneKey(node);
                    ordered.Add((new GodotBoneKey(node, bone), parentKey, skeletonBones.Poses[bone], skeletonBones.Names[bone]));
                }
            }
        }

        var indices = ordered.Select((entry, index) => (entry.Key, index)).ToDictionary(pair => pair.Key, pair => pair.index);
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        var bonesList = ordered.Select((entry, index) => new Bone(
                index,
                UniqueName(names, entry.Name),
                entry.Parent is { } parent && indices.TryGetValue(parent, out var parentIndex) ? parentIndex : -1,
                ToTransform(entry.Local)))
            .ToArray();
        return new GodotSkeleton(new Skeleton(bonesList), indices);
    }

    // The bone a node's children hang from: a bone attachment's bone, or the node itself.
    private GodotBoneKey ParentKey(GodotSceneNode node)
    {
        return AttachedBone(node) is { } attached ? new GodotBoneKey(attached.Bones.Node, attached.Bone) : new GodotBoneKey(node);
    }

    // A BoneAttachment3D under a Skeleton3D follows one of its bones.
    private (GodotSkeletonBones Bones, int Bone)? AttachedBone(GodotSceneNode node)
    {
        if (node.Type != "BoneAttachment3D" || node.Parent is not { Type: "Skeleton3D" } skeletonNode)
        {
            return null;
        }

        if (!_skeletons.TryGetValue(skeletonNode, out var bones))
        {
            bones = new GodotSkeletonBones(skeletonNode);
            _skeletons[skeletonNode] = bones;
        }

        var bone = node["bone_name"] is string boneName ? bones.IndexOf(boneName) : node["bone_idx"] is long index ? (int)index : -1;
        return bone >= 0 && bone < bones.Count ? (bones, bone) : null;
    }

    // Where a node is in the scene; bone attachments sit on their bone.
    private Matrix4x4 World(GodotSceneNode node)
    {
        if (AttachedBone(node) is { } attached)
        {
            return attached.Bones.Globals[attached.Bone] * World(attached.Bones.Node);
        }

        return node.Parent is null ? node.Local : node.Local * World(node.Parent);
    }

    private Mesh BuildSkinnedMesh(GodotSceneNode node, GodotSurface[] surfaces, GodotSkeletonBones skeletonBones, GodotSkeleton skeleton)
    {
        var meshWorld = World(node);
        var skin = loader.Get(node["skin"]);
        var binds = ReadBinds(skin, skeletonBones);
        var skinMatrices = binds.Select(bind => bind.Pose * skeletonBones.Globals[bind.Bone] * meshWorld).ToArray();
        var boneIndices = binds.Select(bind => skeleton.IndexOf(new GodotBoneKey(skeletonBones.Node, bind.Bone))).ToArray();
        var rigidBone = skeleton.IndexOf(ParentKey(node));

        return BuildSdkMesh(node, surfaces, (surface, vertex) =>
        {
            if (!surface.IsSkinned)
            {
                return (meshWorld, [new VertexBoneWeight(rigidBone, 1f)]);
            }

            var matrix = new Matrix4x4();
            List<VertexBoneWeight> weights = new(surface.InfluencesPerVertex);
            var total = 0f;
            for (var slot = 0; slot < surface.InfluencesPerVertex; slot++)
            {
                var influence = (vertex * surface.InfluencesPerVertex) + slot;
                var bind = surface.Bones![influence];
                var weight = surface.Weights![influence];
                if (weight <= 0f || bind >= binds.Length)
                {
                    continue;
                }

                matrix += skinMatrices[bind] * weight;
                total += weight;
                weights.Add(new VertexBoneWeight(boneIndices[bind], weight));
            }

            return total <= 0f
                ? (meshWorld, [new VertexBoneWeight(rigidBone, 1f)])
                : (matrix * (1f / total), MergeWeights(weights, total));
        }, mirrored: false);
    }

    private Mesh BuildRigidMesh(GodotSceneNode node, GodotSurface[] surfaces, GodotSkeleton? skeleton)
    {
        var world = World(node);
        IReadOnlyList<VertexBoneWeight>? weights = skeleton is null ? null : [new VertexBoneWeight(skeleton.IndexOf(ParentKey(node)), 1f)];
        return BuildSdkMesh(node, surfaces, (_, _) => (world, weights), mirrored: world.GetDeterminant() < 0f);
    }

    // A Skin's binds: the skeleton bone each is for and the transform from mesh to bone space.
    // Without a skin, Godot binds each skeleton bone to its own index at its pose.
    private static (int Bone, Matrix4x4 Pose)[] ReadBinds(GodotResource? skin, GodotSkeletonBones bones)
    {
        if (skin is null)
        {
            return [.. Enumerable.Range(0, bones.Count).Select(bone => (bone, Matrix4x4.Invert(bones.Globals[bone], out var inverse) ? inverse : Matrix4x4.Identity))];
        }

        var count = skin["bind_count"] is long bindCount ? (int)bindCount : 0;
        var binds = new (int, Matrix4x4)[count];
        for (var bind = 0; bind < count; bind++)
        {
            var prefix = string.Create(CultureInfo.InvariantCulture, $"bind/{bind}/");
            var bone = skin[prefix + "bone"] is long index && index >= 0 && index < bones.Count
                ? (int)index
                : skin[prefix + "name"] is string name ? bones.IndexOf(name) : -1;
            var pose = skin[prefix + "pose"] is GodotTransform3D transform ? transform.ToMatrix() : Matrix4x4.Identity;
            binds[bind] = (Math.Max(bone, 0), pose);
        }

        return binds;
    }

    // One SDK mesh for a mesh instance: a submesh per surface, each with the material the node
    // or the mesh gives it. Godot's front faces wind clockwise, so triangles are reversed, unless
    // a mirroring transform has already turned them.
    private Mesh BuildSdkMesh(
        GodotSceneNode node,
        GodotSurface[] surfaces,
        Func<GodotSurface, int, (Matrix4x4 Transform, IReadOnlyList<VertexBoneWeight>? Weights)> place,
        bool mirrored)
    {
        List<Vertex> vertices = [];
        List<Submesh> submeshes = [];
        for (var index = 0; index < surfaces.Length; index++)
        {
            var surface = surfaces[index];
            var material = _materials.Build(loader.Get(node["material_override"]) ??
                loader.Get(node[string.Create(CultureInfo.InvariantCulture, $"surface_material_override/{index}")]) ??
                surface.Material);
            var start = vertices.Count;
            for (var vertex = 0; vertex < surface.VertexCount; vertex++)
            {
                var (transform, weights) = place(surface, vertex);
                var position = Vector3.Transform(surface.Positions[vertex], transform);
                var normal = surface.Normals is { } normals ? Vector3.TransformNormal(normals[vertex], transform) : Vector3.UnitY;
                normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
                var uv = surface.TextureCoordinates is { } uvs ? material.Apply(uvs[vertex]) : Vector2.Zero;
                vertices.Add(new Vertex(GodotSpace.Position(position), GodotSpace.Direction(normal), GodotSpace.TextureCoordinate(uv), weights));
            }

            List<Triangle> triangles = new(surface.Indices.Length / 3);
            for (var corner = 0; corner + 2 < surface.Indices.Length; corner += 3)
            {
                var a = start + surface.Indices[corner];
                var b = start + surface.Indices[corner + 1];
                var c = start + surface.Indices[corner + 2];
                triangles.Add(mirrored ? new Triangle(a, b, c) : new Triangle(a, c, b));
            }

            submeshes.Add(new Submesh(material.Material.Name, triangles));
        }

        return new Mesh(vertices, submeshes, node.Name);
    }

    private static Transform ToTransform(Matrix4x4 local)
    {
        Matrix4x4.Decompose(local, out var scale, out var rotation, out var translation);
        return new Transform(GodotSpace.Position(translation), GodotSpace.Rotation(rotation), GodotSpace.Scale(scale));
    }

    private static List<VertexBoneWeight> MergeWeights(List<VertexBoneWeight> weights, float total)
    {
        return [.. weights
            .GroupBy(weight => weight.BoneIndex)
            .Select(group => new VertexBoneWeight(group.Key, group.Sum(weight => weight.Weight) / total))];
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
