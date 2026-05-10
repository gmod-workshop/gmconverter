namespace GMConverter.Source;

internal enum PhysicsMode
{
    Bounds,
    Coacd
}

internal sealed record PhysicsOptions(PhysicsMode Mode, float Mass, CoacdOptions? Coacd);

internal sealed record CoacdOptions(
    float Threshold,
    int MaxConvexPieces,
    int MaxHullVertices);
