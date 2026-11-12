namespace GMConverter.SDK.Geometry;

/// <summary>A bone of a <see cref="Skeleton"/>.</summary>
/// <param name="Index">The bone's position in <see cref="Skeleton.Bones"/>.</param>
/// <param name="Name">The bone's name, unique within the skeleton.</param>
/// <param name="ParentIndex">The parent bone's index, or -1 for a root.</param>
/// <param name="LocalBindPose">The bone's transform relative to its parent in the bind pose.</param>
/// <param name="Humanoid">
/// What the bone is in a human body, when known, so animations can be retargeted between skeletons.
/// </param>
/// <param name="Shared">
/// True for a bone other models follow by name (such as player hands following a view model), so
/// exporters keep it even when no vertex uses it.
/// </param>
/// <param name="HiddenAtRest">
/// True when the bone, with everything on it and below it, is not drawn unless an animation shows
/// it (see <see cref="Animation.BoneVisibilityTrack"/>), such as a prop that only appears in a hand
/// while it is being used.
/// </param>
public sealed record Bone(
    int Index,
    string Name,
    int ParentIndex,
    Transform LocalBindPose,
    HumanoidBone? Humanoid = null,
    bool Shared = false,
    bool HiddenAtRest = false);
