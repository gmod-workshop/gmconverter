using System.Numerics;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Textures;

namespace GMConverter.SourceEngine;

internal static class SourceMaterialEmission
{
    // Identity flow map resolution for the emissive blend pass: 8 bits per channel already limit
    // the mapped coordinate to 1/255, so a larger map would add size without adding precision.
    private const int _flowMapSize = 256;
    private const string _flowMapName = "gmconverter_flow";

    public enum Mode
    {
        None,

        // Emissive layer with its own scroll: VertexLitGeneric samples $selfillummask with the base
        // coordinates, so the layer is written as an unlit additive detail ($detailblendmode 5),
        // whose $detailtexturetransform the proxies scroll.
        DetailLayer,

        // Opaque static glow: coverage goes in the base texture's alpha with plain $selfillum, the
        // long-standing setup every Source branch honours. Garry's Mod reads self-illumination from
        // an alpha channel, so a separate mask left the base alpha at 255 and lit the whole model.
        BaseAlpha,

        // Translucent static glow: base alpha is opacity, so coverage goes in a $selfillummask with
        // every channel (alpha included) set, covering branches that read either.
        Mask,

        // Colour scrolling under a fixed mask (MaterialEmissiveLayer): VertexLitGeneric's emissive
        // blend pass adds base(uv) x emissive(flow(uv) + scroll x time). The pass base is the mask,
        // the emissive texture is the colour, and an identity flow map feeds it the mesh UVs. On
        // opaque materials $selfillum with a black tint first blacks out the masked area, so the
        // additive pass replaces the lit surface there like the source material's blend does.
        EmissiveBlend,
    }

    // A texture the mode needs besides the base texture. Exact textures are sampled as data (the
    // flow map's texels are coordinates), so they must skip block compression and mipmaps and
    // clamp at the edges instead of wrapping. A texture identical for every material names its
    // one shared file with SharedBasename instead of "<material><suffix>".
    public sealed record ExtraTexture(string Suffix, Texture Texture, bool Exact = false, string? SharedBasename = null);

    public static Mode For(Material material)
    {
        if (material.DiffuseTexture is null)
        {
            return Mode.None;
        }

        if (material.EmissiveLayer is not null)
        {
            return Mode.EmissiveBlend;
        }

        if (!material.IsIlluminated)
        {
            return Mode.None;
        }

        if (material.EmissiveUvScrollRate is { } scroll && scroll != Vector2.Zero)
        {
            return Mode.DetailLayer;
        }

        return material.HasAlpha ? Mode.Mask : Mode.BaseAlpha;
    }

    public static bool UsesDetailLayer(Material material) => For(material) == Mode.DetailLayer;

    // Base texture to write for the material: the diffuse, with glow coverage in alpha for
    // BaseAlpha and for an opaque EmissiveBlend's blackout.
    public static Texture BaseTexture(Material material, ITextureFactory textureFactory)
    {
        var diffuse = material.DiffuseTexture!;
        var mode = For(material);
        if (mode != Mode.BaseAlpha && !(mode == Mode.EmissiveBlend && !material.HasAlpha))
        {
            return diffuse;
        }

        var pixels = diffuse.GetRgbaPixels();
        var coverage = mode == Mode.EmissiveBlend ? LayerCoverage(material) : Coverage(material);
        for (var i = 0; i < coverage.Length; i++)
        {
            pixels[(i * 4) + 3] = coverage[i];
        }

        return textureFactory.FromRgba(diffuse.Name, diffuse.Width, diffuse.Height, pixels, hasAlpha: true);
    }

    // Textures to write next to the base texture, each as "<material><suffix>".
    public static IReadOnlyList<ExtraTexture> ExtraTextures(Material material, ITextureFactory textureFactory)
    {
        switch (For(material))
        {
            case Mode.DetailLayer:
                return [new ExtraTexture("_illum", material.EmissiveTexture!)];
            case Mode.Mask:
                var diffuse = material.DiffuseTexture!;
                var coverage = Coverage(material);
                var pixels = new byte[coverage.Length * 4];
                for (var i = 0; i < coverage.Length; i++)
                {
                    pixels.AsSpan(i * 4, 4).Fill(coverage[i]);
                }

                return [new ExtraTexture("_illum", textureFactory.FromRgba($"{diffuse.Name}_illum", diffuse.Width, diffuse.Height, pixels, hasAlpha: true))];
            case Mode.EmissiveBlend:
                var layer = material.EmissiveLayer!;
                return
                [
                    new ExtraTexture("_glowmask", GlowMask(layer.Mask, textureFactory)),
                    new ExtraTexture("_glow", layer.Color),
                    new ExtraTexture("_flow", FlowMap(textureFactory), Exact: true, SharedBasename: _flowMapName),
                ];
            default:
                return [];
        }
    }

    // texturePath maps an ExtraTexture suffix to the texture's material path.
    public static void Write(StreamWriter writer, Material material, Func<string, string> texturePath)
    {
        switch (For(material))
        {
            case Mode.DetailLayer:
                writer.WriteLine(FormattableString.Invariant($"    \"$detail\" \"{texturePath("_illum")}\""));
                writer.WriteLine("    \"$detailblendmode\" \"5\"");
                writer.WriteLine("    \"$detailscale\" \"1\"");
                break;
            case Mode.BaseAlpha:
                writer.WriteLine("    \"$selfillum\" \"1\"");
                break;
            case Mode.Mask:
                writer.WriteLine("    \"$selfillum\" \"1\"");
                writer.WriteLine(FormattableString.Invariant($"    \"$selfillummask\" \"{texturePath("_illum")}\""));
                break;
            case Mode.EmissiveBlend:
                if (!material.HasAlpha)
                {
                    writer.WriteLine("    \"$selfillum\" \"1\"");
                    writer.WriteLine("    \"$selfillumtint\" \"[0 0 0]\"");
                }

                var scroll = material.EmissiveLayer!.ScrollRate;
                writer.WriteLine("    \"$emissiveblendenabled\" \"1\"");
                writer.WriteLine("    \"$emissiveblendstrength\" \"1\"");
                writer.WriteLine("    \"$emissiveblendtint\" \"[1 1 1]\"");
                writer.WriteLine(FormattableString.Invariant($"    \"$emissiveblendbasetexture\" \"{texturePath("_glowmask")}\""));
                writer.WriteLine(FormattableString.Invariant($"    \"$emissiveblendtexture\" \"{texturePath("_glow")}\""));
                writer.WriteLine(FormattableString.Invariant($"    \"$emissiveblendflowtexture\" \"{texturePath("_flow")}\""));
                writer.WriteLine(FormattableString.Invariant($"    \"$emissiveblendscrollvector\" \"[{scroll.X:0.######} {scroll.Y:0.######}]\""));
                break;
        }
    }

    // $selfillum lerps toward the base colour, so coverage is how much of the albedo the emissive
    // texture shows: max(emissive) / max(albedo), per pixel at the diffuse resolution. For a UE2
    // glow baked as albedo x mask alpha this recovers the mask exactly; brighter-than-albedo
    // emission clamps to fully lit.
    private static byte[] Coverage(Material material)
    {
        var diffuse = material.DiffuseTexture!;
        var emissive = material.EmissiveTexture!;
        var albedo = diffuse.GetRgbaPixels();
        var glow = emissive.GetRgbaPixels();
        var coverage = new byte[diffuse.Width * diffuse.Height];
        for (var y = 0; y < diffuse.Height; y++)
        {
            var glowY = Math.Min(emissive.Height - 1, y * emissive.Height / diffuse.Height);
            for (var x = 0; x < diffuse.Width; x++)
            {
                var glowX = Math.Min(emissive.Width - 1, x * emissive.Width / diffuse.Width);
                var g = ((glowY * emissive.Width) + glowX) * 4;
                var a = ((y * diffuse.Width) + x) * 4;
                var glowPeak = Math.Max(glow[g], Math.Max(glow[g + 1], glow[g + 2]));
                var albedoPeak = Math.Max(albedo[a], Math.Max(albedo[a + 1], albedo[a + 2]));
                coverage[(y * diffuse.Width) + x] = glowPeak == 0
                    ? (byte)0
                    : (byte)Math.Min(255, (int)MathF.Round(glowPeak * 255f / Math.Max(albedoPeak, (byte)1)));
            }
        }

        return coverage;
    }

    // The layer mask's alpha resampled (nearest) to the diffuse resolution.
    private static byte[] LayerCoverage(Material material)
    {
        var diffuse = material.DiffuseTexture!;
        var mask = material.EmissiveLayer!.Mask;
        var maskPixels = mask.GetRgbaPixels();
        var coverage = new byte[diffuse.Width * diffuse.Height];
        for (var y = 0; y < diffuse.Height; y++)
        {
            var maskY = Math.Min(mask.Height - 1, y * mask.Height / diffuse.Height);
            for (var x = 0; x < diffuse.Width; x++)
            {
                var maskX = Math.Min(mask.Width - 1, x * mask.Width / diffuse.Width);
                coverage[(y * diffuse.Width) + x] = maskPixels[(((maskY * mask.Width) + maskX) * 4) + 3];
            }
        }

        return coverage;
    }

    // The pass reads its base texture as sRGB and multiplies in linear space, so the mask alpha is
    // stored sRGB-encoded to come back out as the same linear coverage.
    private static Texture GlowMask(Texture mask, ITextureFactory textureFactory)
    {
        var pixels = mask.GetRgbaPixels();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var linear = pixels[i + 3] / 255.0;
            var encoded = linear <= 0.0031308 ? linear * 12.92 : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;
            var value = (byte)Math.Clamp(Math.Round(encoded * 255.0), 0, 255);
            pixels[i] = value;
            pixels[i + 1] = value;
            pixels[i + 2] = value;
            pixels[i + 3] = byte.MaxValue;
        }

        return textureFactory.FromRgba($"{mask.Name}_glowmask", mask.Width, mask.Height, pixels);
    }

    // Red and green hold each texel centre's own U and V, so bilinear filtering hands the pass the
    // mesh UV it was sampled at (within [0, 1]; the map is clamped, not tiled).
    private static Texture FlowMap(ITextureFactory textureFactory)
    {
        var pixels = new byte[_flowMapSize * _flowMapSize * 4];
        for (var y = 0; y < _flowMapSize; y++)
        {
            for (var x = 0; x < _flowMapSize; x++)
            {
                var i = ((y * _flowMapSize) + x) * 4;
                pixels[i] = Centre(x);
                pixels[i + 1] = Centre(y);
                pixels[i + 3] = byte.MaxValue;
            }
        }

        return textureFactory.FromRgba(_flowMapName, _flowMapSize, _flowMapSize, pixels);

        static byte Centre(int texel) => (byte)Math.Round((texel + 0.5) * 255.0 / _flowMapSize);
    }
}
