namespace GMConverter.SDK.Geometry;

public sealed record Submesh(string? MaterialName, IReadOnlyList<Triangle> Triangles);
