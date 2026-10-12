using GMConverter.SDK.Geometry;

namespace GMConverter.SDK.Physics;

/// <summary>A convex collision shape placed relative to its body's bone.</summary>
/// <param name="LocalTransform">The shape's position and orientation relative to the bone. Scale is ignored.</param>
public abstract record CollisionShape(Transform LocalTransform);
