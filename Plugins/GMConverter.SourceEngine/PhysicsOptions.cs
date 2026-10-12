namespace GMConverter.SourceEngine;

internal enum PhysicsMode
{
    // The model's own collision bodies (a ragdoll) when it has them, otherwise Bounds.
    Auto,
    Bounds,
    Coacd
}

internal sealed record PhysicsOptions(PhysicsMode Mode, float Mass, CoacdOptions? Coacd);

internal sealed record CoacdOptions(
    float Threshold,
    int MaxConvexPieces,
    int MaxHullVertices);
