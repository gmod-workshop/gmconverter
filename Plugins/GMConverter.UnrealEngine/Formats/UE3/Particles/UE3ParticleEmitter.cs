using GMConverter.SDK.Particles;
using GMConverter.UnrealEngine.Formats.UE3.Materials;

namespace GMConverter.UnrealEngine.Formats.UE3.Particles;

/// <summary>A Cascade emitter: its settings (with a placeholder material), the material it draws and, for mesh emitters, the static mesh.</summary>
internal sealed record UE3ParticleEmitter(ParticleEmitter Settings, UE3ObjectReference? Material, UE3ObjectReference? Mesh)
{
    /// <summary>The dynamic parameter module's four channels over each particle's life, null where unset.</summary>
    public IReadOnlyList<ParticleFloat?> DynamicParameters { get; init; } = [null, null, null, null];
}
