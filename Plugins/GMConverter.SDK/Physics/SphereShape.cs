using GMConverter.SDK.Geometry;

namespace GMConverter.SDK.Physics;

/// <summary>A sphere centred on the shape's origin.</summary>
/// <param name="LocalTransform">The shape's position and orientation relative to its bone. Scale is ignored.</param>
/// <param name="Radius">The sphere's radius.</param>
public sealed record SphereShape(Transform LocalTransform, float Radius) : CollisionShape(LocalTransform);
