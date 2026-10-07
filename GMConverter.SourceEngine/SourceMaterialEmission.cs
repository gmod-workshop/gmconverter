using System.Numerics;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Textures;

namespace GMConverter.SourceEngine;

internal static class SourceMaterialEmission
{
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
    }

    public static Mode For(Material material)
    {
        if (!material.IsIlluminated || material.DiffuseTexture is null)
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

    // Base texture to write for the material: the diffuse, with glow coverage in alpha for BaseAlpha.
    public static Texture BaseTexture(Material material, ITextureFactory textureFactory)
    {
        var diffuse = material.DiffuseTexture!;
        if (For(material) != Mode.BaseAlpha)
        {
            return diffuse;
        }

        var pixels = diffuse.GetRgbaPixels();
        var coverage = Coverage(material);
        for (var i = 0; i < coverage.Length; i++)
        {
            pixels[(i * 4) + 3] = coverage[i];
        }

        return textureFactory.FromRgba(diffuse.Name, diffuse.Width, diffuse.Height, pixels, hasAlpha: true);
    }

    // Texture to write as "_illum", or null when the mode needs none.
    public static Texture? IllumTexture(Material material, ITextureFactory textureFactory)
    {
        switch (For(material))
        {
            case Mode.DetailLayer:
                return material.EmissiveTexture;
            case Mode.Mask:
                var diffuse = material.DiffuseTexture!;
                var coverage = Coverage(material);
                var pixels = new byte[coverage.Length * 4];
                for (var i = 0; i < coverage.Length; i++)
                {
                    pixels.AsSpan(i * 4, 4).Fill(coverage[i]);
                }

                return textureFactory.FromRgba($"{diffuse.Name}_illum", diffuse.Width, diffuse.Height, pixels, hasAlpha: true);
            default:
                return null;
        }
    }

    public static void Write(StreamWriter writer, Material material, string? illumTexturePath)
    {
        switch (For(material))
        {
            case Mode.DetailLayer when illumTexturePath is not null:
                writer.WriteLine(FormattableString.Invariant($"    \"$detail\" \"{illumTexturePath}\""));
                writer.WriteLine("    \"$detailblendmode\" \"5\"");
                writer.WriteLine("    \"$detailscale\" \"1\"");
                break;
            case Mode.BaseAlpha:
                writer.WriteLine("    \"$selfillum\" \"1\"");
                break;
            case Mode.Mask when illumTexturePath is not null:
                writer.WriteLine("    \"$selfillum\" \"1\"");
                writer.WriteLine(FormattableString.Invariant($"    \"$selfillummask\" \"{illumTexturePath}\""));
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
}
