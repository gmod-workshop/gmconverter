namespace GMConverter.SDK.Geometry;

/// <summary>
/// A skeleton bone with its bind pose relative to its parent. <c>Humanoid</c> says what the bone is
/// in a human body, when known, so animations can be retargeted between skeletons. <c>Shared</c>
/// marks a bone other models follow by name (such as player hands following a view model), so
/// exporters keep it even when no vertex uses it.
/// </summary>
public sealed record Bone(
    int Index,
    string Name,
    int ParentIndex,
    Transform LocalBindPose,
    HumanoidBone? Humanoid = null,
    bool Shared = false);
