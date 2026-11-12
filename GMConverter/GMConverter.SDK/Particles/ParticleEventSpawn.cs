namespace GMConverter.SDK.Particles;

/// <summary>
/// Particles an emitter releases where other emitters' particles are born or die, on top of its own
/// rate and bursts. Each event releases <see cref="Count"/> particles (evaluated at the receiving
/// emitter's time and rounded down) at the particle that raised it, offset by the receiver's
/// <see cref="ParticleEmitter.StartLocation"/>.
/// </summary>
/// <param name="Sources">The emitters, and which of their particles' events, that release these.</param>
/// <param name="Count">How many particles each event releases.</param>
public sealed record ParticleEventSpawn(IReadOnlyList<ParticleEventSource> Sources, ParticleFloat Count);
