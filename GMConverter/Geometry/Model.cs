using System.Numerics;

namespace GMConverter.Geometry;

internal sealed record Model(
    string Name,
    IReadOnlyList<Mesh> Meshes,
    IReadOnlyList<Material> Materials,
    Skeleton? Skeleton = null,
    IReadOnlyList<AnimationClip>? Animations = null)
{
    public IReadOnlyList<Texture> Textures => Materials
        .SelectMany(material => material.Textures)
        .DistinctBy(texture => texture.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public Bounds Bounds()
    {
        return Geometry.Bounds.FromPoints(Meshes.SelectMany(mesh => mesh.Positions).ToArray());
    }

    public Mesh Merge()
    {
        List<Vertex> vertices = [];
        List<Triangle> triangles = [];

        foreach (var mesh in Meshes)
        {
            var vertexOffset = vertices.Count;
            vertices.AddRange(mesh.Vertices.Select(vertex => new Vertex(vertex.Position, Vector3.UnitZ, Vector2.Zero)));

            foreach (var triangle in mesh.Triangles)
            {
                triangles.Add(new Triangle(
                    vertexOffset + triangle.A,
                    vertexOffset + triangle.B,
                    vertexOffset + triangle.C));
            }
        }

        return new Mesh(vertices, [new Submesh(null, triangles)]);
    }

}
