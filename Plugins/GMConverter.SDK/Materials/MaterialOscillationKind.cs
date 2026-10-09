namespace GMConverter.SDK.Materials;

/// <summary>How a <see cref="MaterialOscillation"/> moves texture coordinates along one axis.</summary>
public enum MaterialOscillationKind
{
    /// <summary>Slides back and forth: offset = Amplitude x sin.</summary>
    Pan,

    /// <summary>Stretches about the transform's centre: scale = 1 + Amplitude x sin.</summary>
    Stretch,

    /// <summary>Jumps to a random offset within +/- Amplitude / 2, Rate times per second.</summary>
    Jitter,
}
