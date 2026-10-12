using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.PSK;

/// <param name="Kind">Which of the size fields apply.</param>
/// <param name="Center">The shape's centre relative to its bone.</param>
/// <param name="Rotation">The shape's rotation relative to its bone. Capsules run along its Z axis.</param>
/// <param name="Radius">The sphere or capsule radius.</param>
/// <param name="Length">The capsule's length between the centres of its ends.</param>
/// <param name="HalfExtents">Half the box's size along each of its axes.</param>
/// <param name="Points">The convex hull's points relative to the bone, already placed.</param>
internal sealed record PSKPhysicsShape(
    PSKPhysicsShapeKind Kind,
    Vector3 Center,
    Quaternion Rotation,
    float Radius = 0f,
    float Length = 0f,
    Vector3 HalfExtents = default,
    IReadOnlyList<Vector3>? Points = null);
