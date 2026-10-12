using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.PSK;

/// <summary>
/// A constraint between two bodies. Unreal's twist turns about the frame's X axis, swing 1 about
/// its Z axis and swing 2 about its Y axis. Limits are symmetric half-angles in degrees: null
/// when the axis is free and 0 when it is locked.
/// </summary>
/// <param name="Bone">The child body's bone.</param>
/// <param name="ParentBone">The parent body's bone.</param>
/// <param name="Frame">The constraint's axes relative to the child bone.</param>
/// <param name="TwistLimit">The limit about the frame's X axis.</param>
/// <param name="Swing1Limit">The limit about the frame's Z axis.</param>
/// <param name="Swing2Limit">The limit about the frame's Y axis.</param>
internal sealed record PSKPhysicsJoint(
    string Bone,
    string ParentBone,
    Quaternion Frame,
    float? TwistLimit,
    float? Swing1Limit,
    float? Swing2Limit);
