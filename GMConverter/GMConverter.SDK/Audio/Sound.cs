using GMConverter.SDK.Assets;

namespace GMConverter.SDK.Audio;

/// <summary>
/// Audio from one source asset: the clips it holds and the events that play them. A game sound
/// event (a Wwise event, a SoundCue) imports as one event over its clips; a bare audio file
/// imports as one clip and no events.
/// </summary>
/// <param name="Name">The asset's name, used for default output names.</param>
/// <param name="Clips">The audio, each clip named uniquely within the sound.</param>
/// <param name="Events">What plays the clips. Exporters write these as the target's sound definitions.</param>
public sealed record Sound(string Name, IReadOnlyList<SoundClip> Clips, IReadOnlyList<SoundEvent> Events) : IAsset
{
    public AssetKind Kind => AssetKind.Sound;
}
