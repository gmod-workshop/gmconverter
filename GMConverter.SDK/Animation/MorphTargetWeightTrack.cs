namespace GMConverter.SDK.Animation;

public sealed record MorphTargetWeightTrack(
    string TargetName,
    IReadOnlyList<MorphTargetWeightKeyframe> Keyframes) : IAnimationTrack;
