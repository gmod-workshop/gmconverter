namespace GMConverter.SourceEngine.Geometry;

internal sealed record CoacdDecompositionOptions(
    double Threshold,
    int MaxConvexPieces,
    int MaxHullVertices);
