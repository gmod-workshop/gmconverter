using System.Numerics;
using GMConverter.SDK.Geometry;

namespace GMConverter.SourceEngine.Physics;

// Incremental 3D convex hull. Collision shapes have tens of points, so the quadratic cost is
// irrelevant and the simple version is easier to trust than a full quickhull.
internal static class ConvexHull
{
    // Returns the hull's triangles as indices into points, wound counter-clockwise seen from
    // outside, or null when the points are too few or flat to enclose a volume.
    public static IReadOnlyList<Triangle>? Build(IReadOnlyList<Vector3> points)
    {
        if (points.Count < 4)
        {
            return null;
        }

        var bounds = Bounds.FromPoints([.. points]);
        var epsilon = Vector3.Distance(bounds.Min, bounds.Max) * 1e-5f;
        if (FindInitialTetrahedron(points, epsilon) is not { } tetrahedron)
        {
            return null;
        }

        var (i0, i1, i2, i3) = tetrahedron;
        var inside = (points[i0] + points[i1] + points[i2] + points[i3]) / 4f;
        List<Face> faces = [];
        void AddFace(int a, int b, int c)
        {
            var face = new Face(a, b, c, points);
            faces.Add(face.Distance(inside) > 0f ? new Face(a, c, b, points) : face);
        }

        AddFace(i0, i1, i2);
        AddFace(i0, i1, i3);
        AddFace(i0, i2, i3);
        AddFace(i1, i2, i3);

        for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
        {
            var point = points[pointIndex];
            var visible = faces.Where(face => face.Distance(point) > epsilon).ToArray();
            if (visible.Length == 0)
            {
                continue;
            }

            // The horizon is every edge of the visible region whose reverse edge is not also
            // visible. Each new face keeps the winding of the visible face it replaces.
            HashSet<(int, int)> visibleEdges = [];
            foreach (var face in visible)
            {
                visibleEdges.Add((face.A, face.B));
                visibleEdges.Add((face.B, face.C));
                visibleEdges.Add((face.C, face.A));
            }

            faces.RemoveAll(face => face.Distance(point) > epsilon);
            foreach (var (a, b) in visibleEdges)
            {
                if (!visibleEdges.Contains((b, a)))
                {
                    faces.Add(new Face(a, b, pointIndex, points));
                }
            }
        }

        return [.. faces.Select(face => new Triangle(face.A, face.B, face.C))];
    }

    private static (int, int, int, int)? FindInitialTetrahedron(IReadOnlyList<Vector3> points, float epsilon)
    {
        var i0 = 0;
        for (var i = 1; i < points.Count; i++)
        {
            if (points[i].X < points[i0].X)
            {
                i0 = i;
            }
        }

        var i1 = ArgMax(points, point => Vector3.DistanceSquared(point, points[i0]));
        var axis = points[i1] - points[i0];
        var i2 = ArgMax(points, point => Vector3.Cross(axis, point - points[i0]).LengthSquared());
        var normal = Vector3.Cross(axis, points[i2] - points[i0]);
        if (axis.Length() <= epsilon || normal.Length() <= epsilon * axis.Length())
        {
            return null;
        }

        normal = Vector3.Normalize(normal);
        var i3 = ArgMax(points, point => MathF.Abs(Vector3.Dot(normal, point - points[i0])));
        return MathF.Abs(Vector3.Dot(normal, points[i3] - points[i0])) <= epsilon ? null : (i0, i1, i2, i3);
    }

    private static int ArgMax(IReadOnlyList<Vector3> points, Func<Vector3, float> score)
    {
        var best = 0;
        var bestScore = float.NegativeInfinity;
        for (var i = 0; i < points.Count; i++)
        {
            var value = score(points[i]);
            if (value > bestScore)
            {
                best = i;
                bestScore = value;
            }
        }

        return best;
    }

    private readonly struct Face
    {
        private readonly Vector3 _normal;
        private readonly float _offset;

        public Face(int a, int b, int c, IReadOnlyList<Vector3> points)
        {
            A = a;
            B = b;
            C = c;
            var normal = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
            _normal = normal.LengthSquared() > 0f ? Vector3.Normalize(normal) : Vector3.Zero;
            _offset = Vector3.Dot(_normal, points[a]);
        }

        public int A { get; }

        public int B { get; }

        public int C { get; }

        // Signed distance in front of the face; positive is outside the hull.
        public float Distance(Vector3 point) => Vector3.Dot(_normal, point) - _offset;
    }
}
