namespace GMConverter.UnrealEngine.Formats.UE2.Animations;

internal sealed record UnrealAnimationSequence(
    string Name,
    string Group,
    int FrameCount,
    float Rate,
    IReadOnlyList<UnrealAnimationTrack> Tracks);
