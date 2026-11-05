namespace GMConverter.SDK.Particles;

/// <summary>A point on a <see cref="ParticleFloat"/> curve.</summary>
/// <param name="Time">Where on the curve: seconds into the emitter's cycle, or 0 to 1 through a particle's life, as the property says.</param>
/// <param name="Value">The value there.</param>
public readonly record struct ParticleKey(float Time, float Value);
