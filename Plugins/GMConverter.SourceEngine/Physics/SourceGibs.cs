using System.Numerics;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Physics;

namespace GMConverter.SourceEngine.Physics;

// Cuts a skinned model into rigid pieces along its physics bodies, so it can break apart like a
// prop. Each triangle goes to the body of its vertices' most-weighted bone, or of that bone's
// nearest ancestor with a body. Pieces smaller than the minimum size are folded into their parent
// body's piece (fingers into the hand), leaves first so a chain of small parts folds as one.
//
// A piece is written in its bone's space, because Source places a gib with `placementbone` at
// that bone's current transform: the piece then spawns where it was on the dying model.
internal static class SourceGibs
{
    public static IReadOnlyList<SourceGib> Split(
        Model model,
        Skeleton skeleton,
        ModelPhysics physics,
        IReadOnlyDictionary<int, Matrix4x4> originalWorlds,
        IReadOnlyDictionary<int, Matrix4x4> sourceWorlds,
        float minimumSize)
    {
        var bones = skeleton.Bones.ToDictionary(bone => bone.Index);
        var shapesByBone = physics.Bodies
            .Where(body => bones.ContainsKey(body.BoneIndex))
            .GroupBy(body => body.BoneIndex)
            .ToDictionary(group => group.Key, group => group.SelectMany(body => body.Shapes).ToList());
        if (shapesByBone.Count == 0)
        {
            return [];
        }

        int? ParentBody(int boneIndex)
        {
            for (var index = bones[boneIndex].ParentIndex; index >= 0 && bones.ContainsKey(index); index = bones[index].ParentIndex)
            {
                if (shapesByBone.ContainsKey(index))
                {
                    return index;
                }
            }

            return null;
        }

        int Depth(int boneIndex)
        {
            var depth = 0;
            for (var index = bones[boneIndex].ParentIndex; index >= 0 && bones.ContainsKey(index); index = bones[index].ParentIndex)
            {
                depth++;
            }

            return depth;
        }

        // Geometry above every body (a root bone) goes with the top-most body.
        var rootBody = shapesByBone.Keys.OrderBy(Depth).ThenBy(index => index).First();
        int OwnerBody(int boneIndex) =>
            shapesByBone.ContainsKey(boneIndex) ? boneIndex : ParentBody(boneIndex) ?? rootBody;

        // Each piece is keyed by its own body and collects the bodies folded into it.
        Dictionary<int, List<(int Mesh, string? Material, Triangle Triangle)>> triangles =
            shapesByBone.Keys.ToDictionary(index => index, _ => new List<(int, string?, Triangle)>());
        Dictionary<int, List<int>> members = shapesByBone.Keys.ToDictionary(index => index, index => new List<int> { index });
        for (var meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++)
        {
            var mesh = model.Meshes[meshIndex];
            var owners = mesh.Vertices
                .Select(vertex => OwnerBody(DominantBone(vertex, rootBody, bones)))
                .ToArray();
            foreach (var submesh in mesh.Submeshes)
            {
                foreach (var triangle in submesh.Triangles)
                {
                    var owner = owners[triangle.B] == owners[triangle.C] ? owners[triangle.B] : owners[triangle.A];
                    triangles[owner].Add((meshIndex, submesh.MaterialName, triangle));
                }
            }
        }

        foreach (var boneIndex in shapesByBone.Keys.OrderByDescending(Depth).ToArray())
        {
            if (ParentBody(boneIndex) is not { } parent ||
                triangles[boneIndex].Count == 0 ||
                Size(model, triangles[boneIndex], sourceWorlds[boneIndex]) >= minimumSize)
            {
                continue;
            }

            triangles[parent].AddRange(triangles[boneIndex]);
            members[parent].AddRange(members[boneIndex]);
            triangles.Remove(boneIndex);
            members.Remove(boneIndex);
        }

        List<SourceGib> gibs = [];
        foreach (var (boneIndex, pieceTriangles) in triangles.OrderBy(pair => pair.Key))
        {
            if (pieceTriangles.Count == 0)
            {
                continue;
            }

            Matrix4x4.Invert(sourceWorlds[boneIndex], out var toBone);
            var shapes = members[boneIndex]
                .SelectMany(member => shapesByBone[member].Select(shape => CollisionShapePoints.Get(shape)
                    .Select(point => Vector3.Transform(Vector3.Transform(point, originalWorlds[member]), toBone))
                    .ToArray()))
                .Where(points => ConvexHull.Build(points) is not null)
                .ToArray();
            if (shapes.Length == 0)
            {
                continue;
            }

            var bone = bones[boneIndex];
            var piece = new Model(
                bone.Name,
                [BuildMesh(model, pieceTriangles, toBone)],
                [.. model.Materials.Where(material => pieceTriangles.Any(entry => string.Equals(entry.Material, material.Name, StringComparison.OrdinalIgnoreCase)))],
                new Skeleton([new Bone(0, bone.Name, -1, Transform.Identity)]),
                Skins: model.Skins,
                Physics: new ModelPhysics(
                    [new PhysicsBody(0, [.. shapes.Select(points => new ConvexShape(Transform.Identity, points))])],
                    []));
            gibs.Add(new SourceGib(bone.Name, piece, shapes.Sum(HullVolume)));
        }

        return gibs;
    }

    private static int DominantBone(Vertex vertex, int fallback, Dictionary<int, Bone> bones)
    {
        var weights = vertex.BoneWeights?
            .Where(weight => weight.Weight > 0f && bones.ContainsKey(weight.BoneIndex))
            .GroupBy(weight => weight.BoneIndex)
            .Select(group => (Bone: group.Key, Weight: group.Sum(weight => weight.Weight)))
            .ToArray();
        return weights is { Length: > 0 } ? weights.MaxBy(weight => weight.Weight).Bone : fallback;
    }

    // The edge of a cube as big as the box around a piece in its bone's space, so a long but thin
    // part like a finger counts as small. No side counts as thinner than a tenth of the longest,
    // so a flat panel is still as big as its outline.
    private static float Size(Model model, IEnumerable<(int Mesh, string? Material, Triangle Triangle)> triangles, Matrix4x4 boneWorld)
    {
        Matrix4x4.Invert(boneWorld, out var toBone);
        var bounds = Bounds.FromPoints([.. triangles.SelectMany(entry =>
        {
            var vertices = model.Meshes[entry.Mesh].Vertices;
            return new[] { vertices[entry.Triangle.A].Position, vertices[entry.Triangle.B].Position, vertices[entry.Triangle.C].Position };
        }).Select(position => Vector3.Transform(position, toBone))]);
        var extent = bounds.Max - bounds.Min;
        extent = Vector3.Max(extent, new Vector3(MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z)) / 10f));
        return MathF.Cbrt(extent.X * extent.Y * extent.Z);
    }

    // The piece's triangles as one mesh in the bone's space, one submesh per material.
    private static Mesh BuildMesh(Model model, List<(int Mesh, string? Material, Triangle Triangle)> triangles, Matrix4x4 toBone)
    {
        List<Vertex> vertices = [];
        Dictionary<(int Mesh, int Vertex), int> remap = [];
        int Remap(int meshIndex, int vertexIndex)
        {
            if (!remap.TryGetValue((meshIndex, vertexIndex), out var index))
            {
                var vertex = model.Meshes[meshIndex].Vertices[vertexIndex];
                index = vertices.Count;
                vertices.Add(new Vertex(
                    Vector3.Transform(vertex.Position, toBone),
                    Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, toBone)),
                    vertex.TextureCoordinate));
                remap[(meshIndex, vertexIndex)] = index;
            }

            return index;
        }

        var submeshes = triangles
            .GroupBy(entry => entry.Material, StringComparer.OrdinalIgnoreCase)
            .Select(group => new Submesh(group.Key, [.. group.Select(entry => new Triangle(
                Remap(entry.Mesh, entry.Triangle.A),
                Remap(entry.Mesh, entry.Triangle.B),
                Remap(entry.Mesh, entry.Triangle.C)))]))
            .ToArray();
        return new Mesh(vertices, submeshes);
    }

    // Sums the signed tetrahedra from the origin to each outward-wound face.
    private static float HullVolume(Vector3[] points)
    {
        var triangles = ConvexHull.Build(points) ?? [];
        var volume = triangles.Sum(triangle => Vector3.Dot(points[triangle.A], Vector3.Cross(points[triangle.B], points[triangle.C])));
        return MathF.Abs(volume) / 6f;
    }
}
