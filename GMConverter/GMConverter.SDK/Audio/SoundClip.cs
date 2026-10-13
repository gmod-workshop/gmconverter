namespace GMConverter.SDK.Audio;

/// <summary>Decoded audio.</summary>
/// <param name="Name">The clip's name, unique within its <see cref="Sound"/>. Events refer to clips by it.</param>
/// <param name="SampleRate">Frames per second.</param>
/// <param name="Channels">Samples per frame: 1 for mono, 2 for stereo.</param>
/// <param name="Samples">The samples from -1 to 1, interleaved by channel.</param>
public sealed record SoundClip(string Name, int SampleRate, int Channels, float[] Samples)
{
    /// <summary>The part that repeats when the clip loops, or null when it plays once.</summary>
    public SoundLoop? Loop { get; init; }

    public int FrameCount => Channels == 0 ? 0 : Samples.Length / Channels;

    public double DurationSeconds => SampleRate == 0 ? 0 : (double)FrameCount / SampleRate;
}
