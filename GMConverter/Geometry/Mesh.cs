using System.Numerics;

namespace GMConverter.Geometry;

internal sealed record Mesh(IReadOnlyList<Vertex> Vertices, IReadOnlyList<Submesh> Submeshes)
{
    public IEnumerable<Triangle> Triangles => Submeshes.SelectMany(submesh => submesh.Triangles);
    public IEnumerable<Vector3> Positions => Vertices.Select(vertex => vertex.Position);
}

internal sealed record Submesh(string? MaterialName, IReadOnlyList<Triangle> Triangles);
