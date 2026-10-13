namespace GMConverter.SourceEngine.Physics;

internal sealed record CoacdOptions(
    float Threshold,
    int MaxConvexPieces,
    int MaxHullVertices);
