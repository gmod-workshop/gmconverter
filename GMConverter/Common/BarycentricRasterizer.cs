using System.Numerics;

namespace GMConverter.Common;

// Generic UV-rasterizer with a SIMD-vectorized edge function. Any code path that needs to walk
// the pixels inside a 2D triangle and do something with barycentric coordinates per pixel can use
// this helper instead of rolling its own scan loop. The multi-layer baker is today's only caller,
// but the abstraction is format-agnostic — a future PSK-to-MDL pipeline that wants to bake vertex
// colors, AO maps, or normal-from-tangent-space data would reuse the same loop.
//
// Performance shape:
//   * The edge function (u, v, w computation + inside test) runs Vector&lt;float&gt;.Count pixels at a
//     time on hardware that supports SIMD (8 lanes on AVX2, 4 on SSE2). On a 5800X-class chip
//     that's roughly a 4-8x throughput improvement on the arithmetic portion of the inner loop.
//   * The per-pixel write is invoked through a struct generic constraint
//     (THandler : struct, IBarycentricPixelHandler). The JIT specializes the method per concrete
//     handler type and inlines the Handle call, so we don't pay an interface dispatch per pixel.
//     This is the same pattern .NET itself uses for SortedSet / HashSet specializations.
//   * Triangles smaller than a single SIMD lane (degenerate or sub-pixel) fall through to the
//     scalar tail without any vector overhead.
internal interface IBarycentricPixelHandler
{
    void Handle(int px, int py, float u, float v, float w);
}

internal static class BarycentricRasterizer
{
    // Rasterize the inside of the triangle (p0, p1, p2) against the pixel grid bounded by
    // [minX..maxX] x [minY..maxY]. For each pixel whose center lies inside the triangle, invoke
    // handler.Handle(px, py, u, v, w) where (u, v, w) are barycentric coordinates summing to 1.
    // The handler is passed by ref so a consumer can accumulate state inside the struct if it
    // wants to (pixel counts, sums for averaging, etc.).
    public static void Rasterize<THandler>(
        Vector2 p0,
        Vector2 p1,
        Vector2 p2,
        int minX,
        int maxX,
        int minY,
        int maxY,
        ref THandler handler)
        where THandler : struct, IBarycentricPixelHandler
    {
        if (maxX < minX || maxY < minY)
        {
            return;
        }

        var edge01x = p1.X - p0.X;
        var edge01y = p1.Y - p0.Y;
        var edge02x = p2.X - p0.X;
        var edge02y = p2.Y - p0.Y;
        var det = edge01x * edge02y - edge01y * edge02x;
        if (MathF.Abs(det) < 1e-6f)
        {
            return;
        }

        var invDet = 1f / det;
        var vectorWidth = Vector<float>.Count;
        var useSimd = Vector.IsHardwareAccelerated && vectorWidth > 1;

        // Precompute the lane-offset vector {0, 1, 2, ..., width-1} once. Each inner step folds
        // pxBase + (halfVec - p0xVec) + laneOffsets into the dx vector with one add. Doing the
        // arithmetic this way keeps the dependency chain in the hot loop short.
        Span<float> laneOffsetsBuf = stackalloc float[vectorWidth];
        for (var i = 0; i < vectorWidth; i++)
        {
            laneOffsetsBuf[i] = i;
        }
        var laneOffsets = new Vector<float>(laneOffsetsBuf);
        var halfMinusP0x = new Vector<float>(0.5f - p0.X);
        var dxLaneBase = laneOffsets + halfMinusP0x;

        var edge02yVec = new Vector<float>(edge02y);
        var edge01yVec = new Vector<float>(edge01y);
        var invDetVec = new Vector<float>(invDet);
        var oneVec = Vector<float>.One;
        var zeroVec = Vector<float>.Zero;

        Span<float> uOut = stackalloc float[vectorWidth];
        Span<float> vOut = stackalloc float[vectorWidth];
        Span<float> wOut = stackalloc float[vectorWidth];

        for (var py = minY; py <= maxY; py++)
        {
            var dy = py + 0.5f - p0.Y;
            var dyEdge02x = dy * edge02x;
            var dyEdge01x = dy * edge01x;

            var px = minX;

            if (useSimd)
            {
                var dyEdge02xVec = new Vector<float>(dyEdge02x);
                var dyEdge01xVec = new Vector<float>(dyEdge01x);

                while (px + vectorWidth - 1 <= maxX)
                {
                    var dxVec = new Vector<float>((float)px) + dxLaneBase;
                    var vVec = ((dxVec * edge02yVec) - dyEdge02xVec) * invDetVec;
                    var wVec = (dyEdge01xVec - (dxVec * edge01yVec)) * invDetVec;
                    var uVec = oneVec - vVec - wVec;

                    // Vector.GreaterThanOrEqual on float vectors returns an integer mask of the
                    // same width with -1 bits in lanes where the predicate held and 0 elsewhere.
                    // Bitwise-AND the three masks and short-circuit if every lane is outside —
                    // saves the per-lane dispatch loop when an entire row segment misses.
                    var insideMask = Vector.BitwiseAnd(
                        Vector.BitwiseAnd(
                            Vector.GreaterThanOrEqual(uVec, zeroVec),
                            Vector.GreaterThanOrEqual(vVec, zeroVec)),
                        Vector.GreaterThanOrEqual(wVec, zeroVec));

                    if (!Vector.EqualsAll(insideMask, Vector<int>.Zero))
                    {
                        uVec.CopyTo(uOut);
                        vVec.CopyTo(vOut);
                        wVec.CopyTo(wOut);
                        for (var lane = 0; lane < vectorWidth; lane++)
                        {
                            if (uOut[lane] >= 0f && vOut[lane] >= 0f && wOut[lane] >= 0f)
                            {
                                handler.Handle(px + lane, py, uOut[lane], vOut[lane], wOut[lane]);
                            }
                        }
                    }

                    px += vectorWidth;
                }
            }

            // Scalar tail: covers the case where (maxX - minX + 1) isn't a multiple of vectorWidth,
            // and the no-SIMD path. The math is the same as the per-lane formula above so the
            // boundary pixels match the vector pass exactly — no T-junction or edge artifacts.
            for (; px <= maxX; px++)
            {
                var dx = px + 0.5f - p0.X;
                var v = ((dx * edge02y) - dyEdge02x) * invDet;
                var w = (dyEdge01x - (dx * edge01y)) * invDet;
                var u = 1f - v - w;
                if (u >= 0f && v >= 0f && w >= 0f)
                {
                    handler.Handle(px, py, u, v, w);
                }
            }
        }
    }
}
