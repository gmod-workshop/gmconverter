namespace GMConverter.SDK.Physics;

/// <summary>A rigid collision body that moves with one bone.</summary>
/// <param name="BoneIndex">The bone the body follows.</param>
/// <param name="Shapes">The convex shapes that make up the body, relative to the bone's bind pose.</param>
public sealed record PhysicsBody(int BoneIndex, IReadOnlyList<CollisionShape> Shapes);
