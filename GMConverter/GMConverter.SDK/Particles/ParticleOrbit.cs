namespace GMConverter.SDK.Particles;

/// <summary>
/// Moves each particle's drawn position around its real one: an offset that turns about the
/// particle's axes, as Unreal's orbit module does. Distances in metres, angles in radians.
/// </summary>
/// <param name="Offset">How far from its real position the particle is drawn, picked at spawn.</param>
/// <param name="Rotation">How far the offset starts turned about each axis, picked at spawn.</param>
/// <param name="RotationRate">How fast the offset turns about each axis, in radians per second, picked at spawn.</param>
public sealed record ParticleOrbit(ParticleVector Offset, ParticleVector Rotation, ParticleVector RotationRate);
