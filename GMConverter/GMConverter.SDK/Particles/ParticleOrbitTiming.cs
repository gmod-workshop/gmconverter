namespace GMConverter.SDK.Particles;

/// <summary>When an orbit value is picked from its distribution.</summary>
/// <param name="OnSpawn">Picked once when the particle is released.</param>
/// <param name="EveryFrame">Picked again every frame (see each <see cref="ParticleOrbit"/> value for how it combines).</param>
/// <param name="ByEmitterTime">Looked up by the emitter's time in its cycle rather than the particle's age (0 to 1 over its life).</param>
public sealed record ParticleOrbitTiming(bool OnSpawn, bool EveryFrame, bool ByEmitterTime)
{
    /// <summary>Picked once at spawn, by the particle's age.</summary>
    public static ParticleOrbitTiming SpawnOnly { get; } = new(true, false, false);
}
