using GMConverter.SDK.Particles;
using GMConverter.UnrealEngine.Formats.UE3.Materials;

namespace GMConverter.UnrealEngine.Formats.UE3.Particles;

/// <summary>A Cascade emitter: its settings (with a placeholder material), the material it draws and, for mesh emitters, the mesh's name.</summary>
internal sealed record UE3ParticleEmitter(ParticleEmitter Settings, UE3ObjectReference? Material, string? Mesh);
