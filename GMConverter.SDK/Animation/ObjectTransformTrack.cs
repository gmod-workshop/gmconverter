namespace GMConverter.SDK.Animation;

public sealed record ObjectTransformTrack(
    string TargetName,
    IReadOnlyList<TransformKeyframe> Keyframes) : IAnimationTrack;
