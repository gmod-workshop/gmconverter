namespace GMConverter.SourceEngine.Physics;

internal sealed record CoacdDecompositionOptions(
    double Threshold,
    int MaxConvexPieces,
    int MaxHullVertices);
