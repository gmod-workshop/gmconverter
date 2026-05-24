namespace GMConverter.SDK.Animation;

public sealed record AnimationClip(
    string Name,
    float FrameRate,
    float DurationSeconds,
    IReadOnlyList<IAnimationTrack> Tracks);
