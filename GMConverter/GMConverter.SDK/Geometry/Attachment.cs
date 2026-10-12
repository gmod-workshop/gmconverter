namespace GMConverter.SDK.Geometry;

/// <summary>
/// A named point on a model that other things attach to or are emitted from, such as a weapon in
/// the hand, a muzzle or an engine exhaust. It follows its bone when the model animates.
/// </summary>
/// <param name="Name">The attachment's name.</param>
/// <param name="BoneIndex">The bone it is relative to, or null when it is relative to the model's origin.</param>
/// <param name="LocalTransform">Its position and orientation relative to the bone or origin. Scale is ignored.</param>
public sealed record Attachment(string Name, int? BoneIndex, Transform LocalTransform);
