using GMConverter.SDK.Audio;
using GMConverter.UnrealEngine.Formats.UE3.Materials;

namespace GMConverter.UnrealEngine.Formats.UE3.Audio;

/// <summary>
/// One part of a SoundCue that plays on its own: the waves it picks from (with weights), how loud,
/// at what pitch, after what delay and how far it carries. A cue whose mixer plays several parts at
/// once has a layer per part.
/// </summary>
internal sealed record UE3SoundLayer(
    IReadOnlyList<(UE3ObjectReference Wave, float Weight)> Waves,
    SoundRange Volume,
    SoundRange Pitch,
    float DelaySeconds,
    float? AttenuationMeters,
    bool Loops);
