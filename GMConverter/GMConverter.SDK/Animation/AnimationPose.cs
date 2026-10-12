using GMConverter.SDK.Geometry;

namespace GMConverter.SDK.Animation;

/// <summary>
/// A single skeleton pose: local bone transforms keyed by bone index. Bones that are not listed keep
/// their bind pose. Clips that share a base pose share one instance, so exporters can write it once.
/// </summary>
public sealed record AnimationPose(string Name, IReadOnlyDictionary<int, Transform> BoneTransforms);
