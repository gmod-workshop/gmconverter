namespace GMConverter.Geometry;

internal sealed record AnimationClip(
    string Name,
    float FrameRate,
    float DurationSeconds,
    IReadOnlyList<IAnimationTrack> Tracks);

internal interface IAnimationTrack;

internal sealed record BoneTransformTrack(
    int BoneIndex,
    IReadOnlyList<TransformKeyframe> Keyframes) : IAnimationTrack;

internal sealed record ObjectTransformTrack(
    string TargetName,
    IReadOnlyList<TransformKeyframe> Keyframes) : IAnimationTrack;

internal sealed record MorphTargetWeightTrack(
    string TargetName,
    IReadOnlyList<MorphTargetWeightKeyframe> Keyframes) : IAnimationTrack;

internal readonly record struct TransformKeyframe(float TimeSeconds, Transform Transform);

internal readonly record struct MorphTargetWeightKeyframe(float TimeSeconds, float Weight);
