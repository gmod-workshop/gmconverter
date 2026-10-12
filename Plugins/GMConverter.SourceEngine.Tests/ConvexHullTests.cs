using System.Numerics;
using GMConverter.SourceEngine.Geometry;

namespace GMConverter.SourceEngine.Tests;

public sealed class ConvexHullTests
{
    [Fact]
    public void CubeCornersMakeTwelveOutwardTriangles()
    {
        // The centre point is inside and must not end up on the hull.
        List<Vector3> points = [Vector3.Zero];
        foreach (var x in new[] { -1f, 1f })
        {
            foreach (var y in new[] { -1f, 1f })
            {
                foreach (var z in new[] { -1f, 1f })
                {
                    points.Add(new Vector3(x, y, z));
                }
            }
        }

        var triangles = ConvexHull.Build(points)!;

        Assert.Equal(12, triangles.Count);
        Assert.All(triangles, triangle =>
        {
            Assert.DoesNotContain(0, new[] { triangle.A, triangle.B, triangle.C });
            var center = (points[triangle.A] + points[triangle.B] + points[triangle.C]) / 3f;
            Assert.True(Vector3.Dot(triangle.Normal(points), center) > 0f);
        });
    }

    [Fact]
    public void FlatPointsHaveNoHull()
    {
        Assert.Null(ConvexHull.Build([Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.One with { Z = 0f }]));
    }
}
