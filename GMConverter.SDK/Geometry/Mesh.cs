using System.Numerics;

namespace GMConverter.SDK.Geometry;

public sealed record Mesh(IReadOnlyList<Vertex> Vertices, IReadOnlyList<Submesh> Submeshes, string? Name = null)
{
    public IEnumerable<Triangle> Triangles => Submeshes.SelectMany(submesh => submesh.Triangles);
    public IEnumerable<Vector3> Positions => Vertices.Select(vertex => vertex.Position);
}
