using System.Numerics;
using GMConverter.SDK.Geometry;

namespace GMConverter.SDK.Physics;

/// <summary>The convex hull of a set of points.</summary>
/// <param name="LocalTransform">The shape's position and orientation relative to its bone. Scale is ignored.</param>
/// <param name="Points">The points, relative to the shape's origin.</param>
public sealed record ConvexShape(Transform LocalTransform, IReadOnlyList<Vector3> Points) : CollisionShape(LocalTransform);
