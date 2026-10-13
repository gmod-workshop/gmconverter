using System.Runtime.InteropServices;
using GMConverter.SDK.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GMConverter.SourceEngine.Materials;

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
    /// Builds a Source <c>$phongexponenttexture</c> from Unreal SpecularMasks packing
    /// (R=Specular, G=Metallic, B=Roughness): the exponent in R and B, inverting roughness so
    /// smooth surfaces produce sharp highlights; metalness in G, which <c>$phongalbedotint</c>
    /// reads to tint a metal's highlights by its colour; and the specular in alpha.
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
            output[i + 1] = src[i + 1];
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

    /// <summary>
    /// Darkens <paramref name="texture"/> where the SpecularMasks texture (G = metallic) says it is
    /// metal. A metal's base colour is the colour of its reflections, and Source has no metalness,
    /// so left as it is a polished silver surface would render as flat white paint. Half the colour
    /// is kept, since many maps have no cubemap to reflect.
    /// </summary>
    public static Texture WithMetalDarkened(this Texture texture, Texture specularMasks, ITextureFactory factory)
    {
        var src = texture.GetRgbaPixels();
        var masks = specularMasks.Width == texture.Width && specularMasks.Height == texture.Height
            ? specularMasks.GetRgbaPixels()
            : ResizeRgba(specularMasks.GetRgbaPixels(), specularMasks.Width, specularMasks.Height, texture.Width, texture.Height);

        var output = new byte[src.Length];
        var anyMetal = false;
        for (var i = 0; i + 4 <= src.Length; i += 4)
        {
            var metal = masks[i + 1];
            anyMetal |= metal > 0;
            var keep = 1f - _metalDarkening * metal / byte.MaxValue;
            output[i] = (byte)(src[i] * keep);
            output[i + 1] = (byte)(src[i + 1] * keep);
            output[i + 2] = (byte)(src[i + 2] * keep);
            output[i + 3] = src[i + 3];
        }

        return anyMetal
            ? factory.FromRgba(texture.Name, texture.Width, texture.Height, output, texture.HasAlpha, texture.Path)
            : texture;
    }

    private const float _metalDarkening = 0.5f;

    /// <summary>
    /// The phong and envmap mask for a SpecularMasks texture (G = metallic, B = roughness), in R
    /// for <see cref="WithMaskInAlpha"/>: paint keeps a quarter of its highlight and metal all of
    /// it, and roughness dims a highlight only partly, since it also spreads it.
    /// </summary>
    public static Texture ToSourcePhongMask(this Texture specularMasks, ITextureFactory factory)
    {
        var src = specularMasks.GetRgbaPixels();
        var output = new byte[src.Length];
        for (var i = 0; i + 4 <= src.Length; i += 4)
        {
            var metal = src[i + 1] / 255f;
            var gloss = 1f - src[i + 2] / 255f;
            var mask = (byte)MathF.Round(255f * (0.25f + 0.75f * metal) * (0.4f + 0.6f * gloss));
            output[i] = output[i + 1] = output[i + 2] = mask;
            output[i + 3] = byte.MaxValue;
        }

        return factory.FromRgba($"{specularMasks.Name}_phong_mask", specularMasks.Width, specularMasks.Height, output);
    }

    /// <summary>The average metalness and gloss (1 - roughness) of a SpecularMasks texture, from 0 to 1.</summary>
    public static (float Metalness, float Gloss) AverageMetalAndGloss(this Texture specularMasks)
    {
        var src = specularMasks.GetRgbaPixels();
        double metal = 0, gloss = 0;
        var count = 0;
        // Every 16th texel is plenty for an average and keeps 4K masks quick.
        for (var i = 0; i + 4 <= src.Length; i += 4 * 16)
        {
            metal += src[i + 1];
            gloss += 255 - src[i + 2];
            count++;
        }

        return count == 0 ? (0f, 0f) : ((float)(metal / count / 255), (float)(gloss / count / 255));
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
