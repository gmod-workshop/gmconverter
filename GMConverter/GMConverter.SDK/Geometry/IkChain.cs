namespace GMConverter.SDK.Geometry;

/// <summary>
/// A limb that inverse kinematics can place by its end: a leg ending at the foot or an arm ending
/// at the hand. The chain is the end bone and its two parents (thigh and calf, or upper arm and
/// forearm), which engines with IK use to plant feet on uneven ground or keep a hand on a grip.
/// </summary>
/// <param name="Kind">Which limb the chain is.</param>
/// <param name="EndBoneIndex">The foot or hand bone. It must have a parent and a grandparent.</param>
public sealed record IkChain(IkChainKind Kind, int EndBoneIndex);
