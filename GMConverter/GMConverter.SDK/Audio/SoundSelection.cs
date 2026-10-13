namespace GMConverter.SDK.Audio;

/// <summary>How a <see cref="SoundEvent"/> picks a clip each time it plays.</summary>
public enum SoundSelection
{
    /// <summary>Any entry, by weight.</summary>
    Random,

    /// <summary>Any entry by weight, but not the one played last.</summary>
    RandomNoRepeat,

    /// <summary>Each entry in turn.</summary>
    Sequence,
}
