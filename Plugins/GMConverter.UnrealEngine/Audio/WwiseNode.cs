namespace GMConverter.UnrealEngine.Audio;

/// <summary>
/// A node of a Wwise event's sound structure, reduced to what decides how it sounds: how it picks
/// from its children, its mix, and for a sound, the media it plays.
/// </summary>
/// <param name="Kind">How it plays its children.</param>
/// <param name="Mix">What it adds to the sounds below it.</param>
/// <param name="Children">Its children; empty for a sound.</param>
/// <param name="MediaId">For a sound, the id of the media it plays.</param>
/// <param name="AvoidRepeats">For a random container, whether it avoids replaying recent picks.</param>
internal sealed record WwiseNode(
    WwiseNodeKind Kind,
    WwiseMix Mix,
    IReadOnlyList<WwiseChild> Children,
    uint MediaId = 0,
    bool AvoidRepeats = false)
{
    public static WwiseNode Sound(uint mediaId, WwiseMix? mix = null) => new(WwiseNodeKind.Sound, mix ?? WwiseMix.None, [], mediaId);
}
