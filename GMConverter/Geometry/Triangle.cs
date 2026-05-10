using System.Numerics;

namespace GMConverter.Geometry;

internal readonly record struct Triangle(int A, int B, int C)
{
    public Vector3 Normal(IReadOnlyList<Vertex> vertices)
    {
        return Normal(vertices[A].Position, vertices[B].Position, vertices[C].Position);
    }

    public Vector3 Normal(IReadOnlyList<Vector3> vertices)
    {
        return Normal(vertices[A], vertices[B], vertices[C]);
    }

    private static Vector3 Normal(Vector3 a, Vector3 b, Vector3 c)
    {
        var normal = Vector3.Cross(b - a, c - a);
        var length = normal.Length();

        return length <= 0.000001f ? Vector3.UnitZ : normal / length;
    }
}
