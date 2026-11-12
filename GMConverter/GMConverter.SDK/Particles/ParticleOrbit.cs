namespace GMConverter.SDK.Particles;

/// <summary>
/// Moves each particle's drawn position around its real one, as Unreal's orbit module does: an
/// offset, turned by a rotation that grows at a rate. Distances in metres, angles in radians. The drawn
/// offset is R·offset with R = Rz(z)·Ry(y)·Rx(x) (right-handed turns about the effect's axes, X first),
/// in the particle's space: the effect's for <see cref="ParticleEmitter.LocalSpace"/> emitters, the
/// world's otherwise. Each value is picked as its timing says.
/// </summary>
/// <param name="Offset">How far from its real position the particle is drawn.</param>
/// <param name="Rotation">How far the offset is turned about each axis.</param>
/// <param name="RotationRate">How fast the rotation grows about each axis, in radians per second.</param>
public sealed record ParticleOrbit(ParticleVector Offset, ParticleVector Rotation, ParticleVector RotationRate)
{
    /// <summary>When <see cref="Offset"/> is picked; a value picked every frame is added to the one picked at spawn.</summary>
    public ParticleOrbitTiming OffsetTiming { get; init; } = ParticleOrbitTiming.SpawnOnly;

    /// <summary>When <see cref="Rotation"/> is picked; a value picked every frame is added to the rotation each frame, so it accumulates.</summary>
    public ParticleOrbitTiming RotationTiming { get; init; } = ParticleOrbitTiming.SpawnOnly;

    /// <summary>When <see cref="RotationRate"/> is picked; a value picked every frame is added to the one picked at spawn.</summary>
    public ParticleOrbitTiming RotationRateTiming { get; init; } = ParticleOrbitTiming.SpawnOnly;
}
