namespace GMConverter.SDK.Particles;

/// <summary>A number of particles an emitter releases at once.</summary>
/// <param name="TimeSeconds">When, in seconds into the emitter's cycle.</param>
/// <param name="MinCount">The fewest it releases.</param>
/// <param name="MaxCount">The most it releases; the same as <paramref name="MinCount"/> for a fixed count.</param>
public sealed record ParticleBurst(float TimeSeconds, int MinCount, int MaxCount);
