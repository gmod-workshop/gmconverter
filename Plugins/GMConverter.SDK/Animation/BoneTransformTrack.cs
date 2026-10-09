namespace GMConverter.SDK.Animation;

public sealed record BoneTransformTrack(
    int BoneIndex,
    IReadOnlyList<TransformKeyframe> Keyframes) : IAnimationTrack;
