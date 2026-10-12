namespace GMConverter.SDK.Animation;

/// <summary>
/// A named animation. <see cref="Layering"/> is set when the clip is a layer to add on top of other
/// animation, and null for a clip that plays on its own.
/// </summary>
public sealed record AnimationClip(
    string Name,
    float FrameRate,
    float DurationSeconds,
    IReadOnlyList<IAnimationTrack> Tracks,
    AnimationLayering? Layering = null);
