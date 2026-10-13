namespace GMConverter.UnrealEngine.Materials;

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
