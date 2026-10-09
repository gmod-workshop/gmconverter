using GMConverter.SDK.Textures;

namespace GMConverter.UnrealEngine.Common;

// Pixel versions of the UE2 Combiner operations the material exporter records in sidecars, for
// importers that bake them into single textures. Operands of different sizes are sampled
// nearest-neighbour at the first operand's resolution, since they share texture coordinates.
internal static class CombinerCompositor
{
    // CO_AlphaBlend_With_Mask: lerp(base, overlay, mask alpha). Keeps the base texture's alpha.
    public static Texture BlendWithMask(Texture baseTexture, Texture overlay, Texture mask, ITextureFactory textureFactory)
    {
        var pixels = baseTexture.GetRgbaPixels();
        var overlayPixels = overlay.GetRgbaPixels();
        var maskPixels = mask.GetRgbaPixels();
        for (var y = 0; y < baseTexture.Height; y++)
        {
            for (var x = 0; x < baseTexture.Width; x++)
            {
                var i = ((y * baseTexture.Width) + x) * 4;
                var o = SampleOffset(overlay, x, y, baseTexture.Width, baseTexture.Height);
                var weight = maskPixels[SampleOffset(mask, x, y, baseTexture.Width, baseTexture.Height) + 3] / 255.0;
                for (var c = 0; c < 3; c++)
                {
                    pixels[i + c] = (byte)Math.Round(pixels[i + c] + ((overlayPixels[o + c] - pixels[i + c]) * weight));
                }
            }
        }

        return textureFactory.FromRgba(baseTexture.Name, baseTexture.Width, baseTexture.Height, pixels, baseTexture.HasAlpha, baseTexture.Path);
    }

    // CO_Add / CO_Multiply with a ConstantColor operand, applied to RGBA pixels in place. Multiply
    // scales by the Modulate2X/4X factor in the operation name; results clamp like the fixed-function
    // pipeline UE2 targeted. Returns false for an unknown operation, leaving the pixels untouched.
    public static bool ApplyConstantColor(byte[] pixels, ReadOnlySpan<byte> color, string operation)
    {
        int? factor = operation.ToUpperInvariant() switch
        {
            "MULTIPLY" => 1,
            "MULTIPLY2X" => 2,
            "MULTIPLY4X" => 4,
            _ => null
        };
        var add = operation.Equals("Add", StringComparison.OrdinalIgnoreCase);
        if (factor is null && !add)
        {
            return false;
        }

        for (var i = 0; i < pixels.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
            {
                pixels[i + c] = (byte)Math.Min(255, add
                    ? pixels[i + c] + color[c]
                    : (int)Math.Round(pixels[i + c] * (color[c] / 255.0) * factor!.Value));
            }
        }

        return true;
    }

    private static int SampleOffset(Texture texture, int x, int y, int width, int height)
    {
        var sampleX = Math.Min(texture.Width - 1, x * texture.Width / width);
        var sampleY = Math.Min(texture.Height - 1, y * texture.Height / height);
        return ((sampleY * texture.Width) + sampleX) * 4;
    }
}
