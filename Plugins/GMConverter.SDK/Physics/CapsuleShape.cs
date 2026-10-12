using GMConverter.SDK.Geometry;

namespace GMConverter.SDK.Physics;

/// <summary>A capsule centred on the shape's origin and running along its Z axis.</summary>
/// <param name="LocalTransform">The shape's position and orientation relative to its bone. Scale is ignored.</param>
/// <param name="Radius">The radius of the cylinder and its rounded ends.</param>
/// <param name="Length">The length of the cylinder between the centres of the two ends.</param>
public sealed record CapsuleShape(Transform LocalTransform, float Radius, float Length) : CollisionShape(LocalTransform);
