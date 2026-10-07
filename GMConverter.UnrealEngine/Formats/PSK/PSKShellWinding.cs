using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.PSK;

// Finds closed shells whose triangle winding is inside-out relative to the rest of the mesh, so the
// importer can reverse them. Some game assets ship a mirrored part (e.g. Republic Commando's bacta
// dispenser eyelid) with inverted winding; the original renderer back-face culled it and showed
// the far wall, but exporters derive normals from winding and Source materials are $nocull, so
// the part renders lit from behind (nearly black).
//
// Only closed, consistently wound shells are judged: their signed volume says which way they face.
// A shell is flipped when its sign disagrees with a clear majority (by count) of the mesh's
// closed shells; counting shells rather than faces keeps one large odd-one-out from swinging it.
// Open surfaces have no inside, and PSKX files with authored normals are left untouched.
internal static class PSKShellWinding
{
    private const double _majorityShare = 0.75;
    private const int _minimumAgreeingShells = 3;

    public static HashSet<int> FindInvertedFaces(PSKFile psk)
    {
        if (psk.VertexNormals.Count > 0)
        {
            return [];
        }

        var faces = WeldedFaces(psk);
        var shells = GroupShells(faces);
        var closed = shells
            .Select(shell => (Faces: shell, Sign: ClosedShellSign(shell, faces, psk.Points)))
            .Where(shell => shell.Sign != 0)
            .ToList();

        var positiveShells = closed.Count(shell => shell.Sign > 0);
        var negativeShells = closed.Count - positiveShells;
        var dominantSign = positiveShells >= negativeShells ? 1 : -1;
        var agreeingShells = Math.Max(positiveShells, negativeShells);
        if (agreeingShells < _minimumAgreeingShells || (double)agreeingShells / closed.Count < _majorityShare)
        {
            return [];
        }

        return [.. closed.Where(shell => shell.Sign != dominantSign).SelectMany(shell => shell.Faces)];
    }

    // Face corners as welded point indices (points at identical positions share an index), or null
    // for faces that reference missing data or collapse onto fewer than three points.
    private static int[]?[] WeldedFaces(PSKFile psk)
    {
        Dictionary<Vector3, int> canonical = [];
        var weld = new int[psk.Points.Count];
        for (var i = 0; i < psk.Points.Count; i++)
        {
            weld[i] = canonical.TryGetValue(psk.Points[i], out var existing) ? existing : canonical[psk.Points[i]] = i;
        }

        var faces = new int[]?[psk.Faces.Count];
        for (var f = 0; f < psk.Faces.Count; f++)
        {
            var corners = new int[3];
            var valid = true;
            for (var c = 0; c < 3 && valid; c++)
            {
                var wedgeIndex = psk.Faces[f].WedgeIndices[c];
                valid = wedgeIndex >= 0 && wedgeIndex < psk.Wedges.Count &&
                    psk.Wedges[wedgeIndex].PointIndex >= 0 && psk.Wedges[wedgeIndex].PointIndex < weld.Length;
                if (valid)
                {
                    corners[c] = weld[psk.Wedges[wedgeIndex].PointIndex];
                }
            }

            faces[f] = valid && corners[0] != corners[1] && corners[1] != corners[2] && corners[0] != corners[2]
                ? corners
                : null;
        }

        return faces;
    }

    private static List<List<int>> GroupShells(int[]?[] faces)
    {
        var parent = Enumerable.Range(0, faces.Length).ToArray();
        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        Dictionary<int, int> firstFaceAtPoint = [];
        for (var f = 0; f < faces.Length; f++)
        {
            foreach (var point in faces[f] ?? [])
            {
                if (firstFaceAtPoint.TryGetValue(point, out var other))
                {
                    parent[Find(f)] = Find(other);
                }
                else
                {
                    firstFaceAtPoint[point] = f;
                }
            }
        }

        return [.. Enumerable.Range(0, faces.Length)
            .Where(f => faces[f] is not null)
            .GroupBy(Find)
            .Select(group => group.ToList())];
    }

    // A shell is closed and consistently wound when every directed edge appears exactly once and
    // its reverse also appears; its signed volume then tells which way the winding faces. Returns
    // the volume's sign, or 0 for open, inconsistently wound or flat shells.
    private static int ClosedShellSign(List<int> shell, int[]?[] faces, List<Vector3> points)
    {
        HashSet<(int, int)> directedEdges = [];
        double volume = 0;
        foreach (var corners in shell.Select(f => faces[f]!))
        {
            for (var c = 0; c < 3; c++)
            {
                if (!directedEdges.Add((corners[c], corners[(c + 1) % 3])))
                {
                    return 0;
                }
            }

            var a = points[corners[0]];
            var b = points[corners[1]];
            var d = points[corners[2]];
            volume += Vector3.Dot(a, Vector3.Cross(b, d)) / 6.0;
        }

        if (directedEdges.Any(edge => !directedEdges.Contains((edge.Item2, edge.Item1))) || Math.Abs(volume) < 1e-9)
        {
            return 0;
        }

        return Math.Sign(volume);
    }
}
