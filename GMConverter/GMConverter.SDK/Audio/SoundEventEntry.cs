namespace GMConverter.SDK.Audio;

/// <summary>One clip a <see cref="SoundEvent"/> can play.</summary>
/// <param name="Clip">The <see cref="SoundClip.Name"/> of the clip.</param>
/// <param name="Weight">How likely it is to be picked relative to the other entries.</param>
public sealed record SoundEventEntry(string Clip, float Weight = 1f);
