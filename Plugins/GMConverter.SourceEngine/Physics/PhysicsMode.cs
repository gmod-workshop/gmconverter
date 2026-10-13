namespace GMConverter.SourceEngine.Physics;

internal enum PhysicsMode
{
    // The model's own collision bodies (a ragdoll) when it has them, otherwise Bounds.
    Auto,
    Bounds,
    Coacd
}
