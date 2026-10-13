namespace GMConverter.SDK.Audio;

/// <summary>A value picked at random between <see cref="Min"/> and <see cref="Max"/> each time a sound plays.</summary>
public readonly record struct SoundRange(float Min, float Max)
{
    /// <summary>Always 1: the clip as recorded.</summary>
    public static SoundRange One { get; } = new(1f, 1f);

    public bool IsFixed => Min == Max;

    public static SoundRange Fixed(float value) => new(value, value);
}
