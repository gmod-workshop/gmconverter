using System.Numerics;
using GMConverter.SDK.Physics;

namespace GMConverter.SourceEngine.Geometry;

// Source collision is made of convex hulls, so every shape is reduced to points whose hull
// stands in for it. Round shapes are sampled at 45-degree steps, which keeps each hull small
// while staying within a few percent of the true surface.
internal static class CollisionShapePoints
{
    private const int _segments = 8;

    // Points relative to the shape's bone.
    public static Vector3[] Get(CollisionShape shape)
    {
        var points = shape switch
        {
            SphereShape sphere => Hemisphere(sphere.Radius, 0f, 1f).Concat(Hemisphere(sphere.Radius, 0f, -1f)),
            CapsuleShape capsule => Hemisphere(capsule.Radius, capsule.Length / 2f, 1f).Concat(Hemisphere(capsule.Radius, -capsule.Length / 2f, -1f)),
            BoxShape box => Box(box.HalfExtents),
            ConvexShape convex => convex.Points,
            _ => [],
        };
        var rotation = shape.LocalTransform.Rotation;
        var translation = shape.LocalTransform.Translation;
        return [.. points.Select(point => Vector3.Transform(point, rotation) + translation)];
    }

    // The equator ring, the 45-degree ring and the pole of one half of a sphere, moved along Z.
    private static IEnumerable<Vector3> Hemisphere(float radius, float centerZ, float direction)
    {
        foreach (var latitude in new[] { 0f, MathF.PI / 4f })
        {
            var ringRadius = radius * MathF.Cos(latitude);
            var z = centerZ + (direction * radius * MathF.Sin(latitude));
            for (var segment = 0; segment < _segments; segment++)
            {
                var angle = segment * 2f * MathF.PI / _segments;
                yield return new Vector3(ringRadius * MathF.Cos(angle), ringRadius * MathF.Sin(angle), z);
            }
        }

        yield return new Vector3(0f, 0f, centerZ + (direction * radius));
    }

    private static IEnumerable<Vector3> Box(Vector3 halfExtents)
    {
        foreach (var x in new[] { -1f, 1f })
        {
            foreach (var y in new[] { -1f, 1f })
            {
                foreach (var z in new[] { -1f, 1f })
                {
                    yield return halfExtents * new Vector3(x, y, z);
                }
            }
        }
    }
}
