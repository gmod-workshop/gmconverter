using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.UE3.Materials;

/// <summary>
/// A particle material's texture slid by one of the particle's dynamic parameters: the texture
/// coordinates are <c>uv * Tiling + Speed * parameter</c>, where the parameter is channel
/// <see cref="Channel"/> (0 to 3) of the emitter's dynamic parameter module over the particle's life.
/// </summary>
internal sealed record UE3ParticleUvAnimation(Vector2 Tiling, Vector2 Speed, int Channel);
