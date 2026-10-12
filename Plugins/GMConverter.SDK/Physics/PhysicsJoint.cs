using System.Numerics;

namespace GMConverter.SDK.Physics;

/// <summary>
/// The joint between a body and its parent body. Rotation is measured from the bind pose about
/// the axes of the joint's frame.
/// </summary>
/// <param name="BoneIndex">The bone of the child body.</param>
/// <param name="ParentBoneIndex">The bone of the parent body.</param>
/// <param name="Frame">The orientation of the joint's axes relative to the child bone.</param>
/// <param name="X">The limit about the frame's X axis, or null when it rotates freely.</param>
/// <param name="Y">The limit about the frame's Y axis, or null when it rotates freely.</param>
/// <param name="Z">The limit about the frame's Z axis, or null when it rotates freely.</param>
public sealed record PhysicsJoint(
    int BoneIndex,
    int ParentBoneIndex,
    Quaternion Frame,
    AngularLimit? X,
    AngularLimit? Y,
    AngularLimit? Z);
