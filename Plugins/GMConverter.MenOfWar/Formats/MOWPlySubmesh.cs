using GMConverter.SDK.Geometry;

namespace GMConverter.MenOfWar.Formats;

internal sealed record MOWPlySubmesh(string MaterialFile, IReadOnlyList<Triangle> Triangles);
