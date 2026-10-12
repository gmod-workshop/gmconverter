namespace GMConverter.SDK.Physics;

/// <summary>
/// A model's physical make-up: the collision bodies attached to its bones and the joints that
/// hold them together, which is what a ragdoll needs. Exporters without physics ignore it.
/// </summary>
/// <param name="Bodies">One body per bone that has collision.</param>
/// <param name="Joints">How each body may rotate relative to its parent body.</param>
public sealed record ModelPhysics(IReadOnlyList<PhysicsBody> Bodies, IReadOnlyList<PhysicsJoint> Joints);
