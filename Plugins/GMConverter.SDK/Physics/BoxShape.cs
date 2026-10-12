using System.Numerics;
using GMConverter.SDK.Geometry;

namespace GMConverter.SDK.Physics;

/// <summary>A box centred on the shape's origin.</summary>
/// <param name="LocalTransform">The shape's position and orientation relative to its bone. Scale is ignored.</param>
/// <param name="HalfExtents">Half the box's size along each of the shape's axes.</param>
public sealed record BoxShape(Transform LocalTransform, Vector3 HalfExtents) : CollisionShape(LocalTransform);
