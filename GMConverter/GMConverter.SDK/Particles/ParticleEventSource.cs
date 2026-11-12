namespace GMConverter.SDK.Particles;

/// <summary>An emitter whose particles' births or deaths release another emitter's particles.</summary>
/// <param name="Emitter">The <see cref="ParticleEmitter.Name"/> whose particles raise the event.</param>
/// <param name="Kind">Whether their births or their deaths do.</param>
public sealed record ParticleEventSource(string Emitter, ParticleEventKind Kind);
