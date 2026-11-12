namespace GMConverter.SDK.Animation;

/// <summary>
/// Whether a bone, with everything on it and below it, is drawn during a clip, such as a prop a
/// character picks up partway through. Each keyframe holds until the next; before the first, the
/// bone is as <see cref="Geometry.Bone.HiddenAtRest"/> says.
/// </summary>
public sealed record BoneVisibilityTrack(
    int BoneIndex,
    IReadOnlyList<VisibilityKeyframe> Keyframes) : IAnimationTrack;
