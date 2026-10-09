namespace GMConverter.SDK.Materials;

/// <summary>
/// Periodic movement of texture coordinates along one axis. Rate is in cycles (or, for jitter,
/// jumps) per second, Phase in radians, and Amplitude in texture widths or heights.
/// </summary>
public sealed record MaterialOscillation(MaterialOscillationKind Kind, float Amplitude, float Rate, float Phase = 0f);
