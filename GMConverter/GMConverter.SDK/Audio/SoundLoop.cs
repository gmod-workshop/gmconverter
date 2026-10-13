namespace GMConverter.SDK.Audio;

/// <summary>The part of a <see cref="SoundClip"/> that repeats, in frames.</summary>
/// <param name="StartFrame">The first frame of the loop.</param>
/// <param name="EndFrame">The frame after the loop's last, or null to loop to the end of the clip.</param>
public sealed record SoundLoop(int StartFrame = 0, int? EndFrame = null);
