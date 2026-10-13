namespace GMConverter.SDK.Audio;

/// <summary>
/// Something a game plays by name: a choice of clips, with how loud, at what pitch and how far
/// away it can be heard. It is the engine-neutral form of a Wwise event, an Unreal SoundCue or a
/// Source sound script entry. Animation events name sounds by <see cref="Name"/>.
/// </summary>
/// <param name="Name">The name the source game plays it by, kept so animation events still match.</param>
/// <param name="Entries">The clips it chooses between.</param>
public sealed record SoundEvent(string Name, IReadOnlyList<SoundEventEntry> Entries)
{
    /// <summary>How each play picks from <see cref="Entries"/>.</summary>
    public SoundSelection Selection { get; init; } = SoundSelection.Random;

    /// <summary>Gain each play picks from, where 1 is the clip as recorded.</summary>
    public SoundRange Volume { get; init; } = SoundRange.One;

    /// <summary>Playback rate each play picks from, where 1 is the clip as recorded and 2 an octave up.</summary>
    public SoundRange Pitch { get; init; } = SoundRange.One;

    /// <summary>How far away the sound can be heard, in metres, or null for a sound that is not positioned in the world (music, UI).</summary>
    public float? AttenuationMeters { get; init; }

    /// <summary>What kind of sound it is, so targets can pick a matching channel or mixer group.</summary>
    public SoundCategory Category { get; init; } = SoundCategory.Other;

    /// <summary>Whether the sound keeps playing until stopped.</summary>
    public bool Loops { get; init; }
}
