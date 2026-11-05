using GMConverter.SDK.Assets;

namespace GMConverter.SDK.Particles;

/// <summary>
/// A particle effect a game plays by name: emitters that run together from one place. It is the
/// engine-neutral form of an Unreal Cascade particle system or a Source particle system definition.
/// Animation events name effects by <see cref="Name"/>.
/// </summary>
/// <param name="Name">The name the source game plays it by, kept so animation events still match.</param>
/// <param name="Emitters">The emitters, which all start when the effect does.</param>
public sealed record ParticleEffect(string Name, IReadOnlyList<ParticleEmitter> Emitters) : IAsset
{
    public AssetKind Kind => AssetKind.Particle;
}
