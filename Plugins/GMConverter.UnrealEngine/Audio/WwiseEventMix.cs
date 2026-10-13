using GMConverter.SDK.Audio;

namespace GMConverter.UnrealEngine.Audio;

/// <summary>
/// A Wwise event flattened into what one engine-neutral sound event can say: the media it picks
/// from, and one volume, pitch and attenuation for all of them.
/// </summary>
/// <param name="Entries">The media it picks from.</param>
/// <param name="Mix">The mix of the loudest entry, which every entry shares apart from its own <see cref="WwiseEventEntry.GainDb"/>.</param>
/// <param name="Selection">How it picks.</param>
internal sealed record WwiseEventMix(IReadOnlyList<WwiseEventEntry> Entries, WwiseMix Mix, SoundSelection Selection);
