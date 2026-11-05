namespace GMConverter.SDK.Particles;

/// <summary>Particles an emitter releases where another emitter's particles die.</summary>
/// <param name="Emitter">The <see cref="ParticleEmitter.Name"/> whose particles' deaths release these.</param>
/// <param name="Count">How many it releases per death.</param>
public sealed record ParticleDeathSpawn(string Emitter, ParticleFloat Count);
