namespace GMConverter.UnrealEngine.Formats.PSK.Physics;

/// <param name="Bone">The bone the body follows.</param>
/// <param name="Shapes">The body's collision shapes.</param>
internal sealed record PSKPhysicsBody(string Bone, IReadOnlyList<PSKPhysicsShape> Shapes);
