using System.Runtime.InteropServices;
using GMConverter.SDK.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GMConverter.SourceEngine;

/// <summary>
/// Source-Engine-specific texture transforms. These used to live in Core's
/// <c>TextureExtensions</c> and downcast to <c>ImageSharpTexture</c>; with Source extracted into
/// its own plugin assembly they had to be reimplemented against the SDK <see cref="Texture"/>
/// surface (<see cref="Texture.GetRgbaPixels"/> + <see cref="ITextureFactory.FromRgba"/>). The
/// plugin still uses ImageSharp internally for the resize path when a mask's dimensions don't
/// match the target texture, but the result is handed back to the host via the factory.
/// </summary>
internal static class SourceTextureTransforms
{
    /// <summary>
    /// Builds a Source <c>$phongexponenttexture</c>. Maps Fortnite SpecularMasks packing
    /// (R=Specular, G=Metallic, B=Roughness) to Source's per-pixel exponent scale (RGB) and
    /// phong mask in alpha. Inverts roughness so smooth surfaces produce sharp highlights.
    /// </summary>
    public static Texture ToSourcePhongExponent(this Texture texture, ITextureFactory factory, string? textureName = null)
    {
        var src = texture.GetRgbaPixels();
        var output = new byte[src.Length];
        for (var i = 0; i + 4 <= src.Length; i += 4)
        {
            var r = src[i];
            var b = src[i + 2];
            var exponent = (byte)(byte.MaxValue - b);
            output[i] = exponent;
            output[i + 1] = exponent;
            output[i + 2] = exponent;
            output[i + 3] = r;
        }
        return factory.FromRgba(
            textureName ?? $"{texture.Name}_phong_exponent",
            texture.Width,
            texture.Height,
            output);
    }

    /// <summary>
    /// Returns a copy of <paramref name="texture"/> with its alpha channel replaced by the
    /// mask's red channel. Used for Source's <c>$normalmapalphaenvmapmask</c> workflow, where
    /// the envmap mask must live in the normal map's alpha rather than a separate texture
    /// (Source rejects <c>$envmapmask</c> alongside <c>$bumpmap</c> due to pixel-shader register
    /// limits in VertexLitGeneric). When the mask's dimensions differ from the texture's, the
    /// mask is resized in-plugin via ImageSharp.
    /// </summary>
    public static Texture WithMaskInAlpha(
        this Texture texture,
        Texture mask,
        ITextureFactory factory,
        string? textureName = null)
    {
        var src = texture.GetRgbaPixels();
        var maskPixels = mask.Width == texture.Width && mask.Height == texture.Height
            ? mask.GetRgbaPixels()
            : ResizeRgba(mask.GetRgbaPixels(), mask.Width, mask.Height, texture.Width, texture.Height);

        var output = new byte[src.Length];
        for (var i = 0; i + 4 <= src.Length; i += 4)
        {
            output[i] = src[i];
            output[i + 1] = src[i + 1];
            output[i + 2] = src[i + 2];
            output[i + 3] = maskPixels[i];
        }
        return factory.FromRgba(
            textureName ?? $"{texture.Name}_with_mask",
            texture.Width,
            texture.Height,
            output,
            hasAlpha: true);
    }

    private static byte[] ResizeRgba(byte[] sourceRgba, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        // The plugin has SixLabors.ImageSharp available for its own internal use — we use it here
        // for the resize because reimplementing a quality resampler against raw bytes is more
        // complexity than the value of avoiding the dep. The result still flows back into the
        // host's Texture via ITextureFactory.FromRgba so the host's image library remains
        // pluggable from the SDK's point of view.
        using var image = Image.LoadPixelData<Rgba32>(sourceRgba, sourceWidth, sourceHeight);
        image.Mutate(ctx => ctx.Resize(targetWidth, targetHeight));
        var output = new byte[targetWidth * targetHeight * 4];
        image.CopyPixelDataTo(MemoryMarshal.AsBytes(output.AsSpan()));
        return output;
    }
}
