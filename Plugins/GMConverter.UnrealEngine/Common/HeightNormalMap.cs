namespace GMConverter.UnrealEngine.Common;

// Converts a grayscale height map into a tangent-space normal map. Republic Commando ships its
// shader "Bumpmap" textures as height data rather than normals, while exporters (Source $bumpmap,
// glTF normalTexture) expect normals. Uses a Sobel gradient with wrap-around sampling, since these
// textures tile, and writes OpenGL-convention normals (green = +V up the image) as RGBA8.
internal static class HeightNormalMap
{
    // Slope multiplier applied to heights in [0, 1] per texel; 4 keeps a full-range step across a
    // few texels at roughly 60 degrees without flattening subtle panel detail.
    private const float _strength = 4f;

    public static byte[] FromHeights(byte[] heights, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                float H(int dx, int dy) => Sample(heights, width, height, x + dx, y + dy);

                // Sobel over rows that run down the image; divide by 8 for a per-texel slope.
                var slopeRight = (H(1, -1) + (2 * H(1, 0)) + H(1, 1) - H(-1, -1) - (2 * H(-1, 0)) - H(-1, 1)) / 8f;
                var slopeDown = (H(-1, 1) + (2 * H(0, 1)) + H(1, 1) - H(-1, -1) - (2 * H(0, -1)) - H(1, -1)) / 8f;

                // Surface z = h: normal ∝ (-dh/dx, -dh/dup, 1), and "up" is minus the row direction.
                var nx = -slopeRight * _strength;
                var ny = slopeDown * _strength;
                var length = MathF.Sqrt((nx * nx) + (ny * ny) + 1f);
                var offset = ((y * width) + x) * 4;
                pixels[offset] = Encode(nx / length);
                pixels[offset + 1] = Encode(ny / length);
                pixels[offset + 2] = Encode(1f / length);
                pixels[offset + 3] = byte.MaxValue;
            }
        }

        return pixels;
    }

    private static float Sample(byte[] heights, int width, int height, int x, int y)
    {
        var sx = (x + width) % width;
        var sy = (y + height) % height;
        return heights[(sy * width) + sx] / 255f;
    }

    private static byte Encode(float component)
    {
        return (byte)Math.Clamp(MathF.Round(((component * 0.5f) + 0.5f) * 255f), 0f, 255f);
    }
}
