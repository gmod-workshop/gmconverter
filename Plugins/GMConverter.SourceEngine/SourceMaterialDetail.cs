using GMConverter.SDK.Materials;
using GMConverter.SDK.Textures;

namespace GMConverter.SourceEngine;

// A MaterialLayer becomes VertexLitGeneric's $detail texture, which combines with the base before
// lighting through its own $detailtexturetransform. A material whose scrolling glow already
// occupies $detail (SourceMaterialEmission.Mode.DetailLayer) keeps the glow and drops the layer.
internal static class SourceMaterialDetail
{
    // $detailblendmode values: 0 is base x detail x 2, 1 is base + detail.
    private const int _blendModeMod2X = 0;
    private const int _blendModeAdditive = 1;

    public static MaterialLayer? LayerFor(Material material)
    {
        return material.DetailLayer is not null &&
            material.DiffuseTexture is not null &&
            !SourceMaterialEmission.UsesDetailLayer(material)
            ? material.DetailLayer
            : null;
    }

    // Mod2X doubles the product, so a plain multiply is written at half brightness and a 4X
    // multiply at double (clamped).
    public static IReadOnlyList<SourceMaterialEmission.ExtraTexture> ExtraTextures(Material material, ITextureFactory textureFactory)
    {
        if (LayerFor(material) is not { } layer)
        {
            return [];
        }

        var scale = layer.Blend switch
        {
            MaterialLayerBlend.Multiply => 0.5,
            MaterialLayerBlend.Multiply4X => 2.0,
            _ => 1.0
        };
        var texture = layer.Texture;
        if (layer.Blend is MaterialLayerBlend.Multiply or MaterialLayerBlend.Multiply4X)
        {
            var pixels = texture.GetRgbaPixels();
            for (var i = 0; i < pixels.Length; i += 4)
            {
                for (var c = 0; c < 3; c++)
                {
                    pixels[i + c] = (byte)Math.Min(255, Math.Round(pixels[i + c] * scale));
                }
            }

            texture = textureFactory.FromRgba($"{texture.Name}_detail", texture.Width, texture.Height, pixels, texture.HasAlpha);
        }

        return [new SourceMaterialEmission.ExtraTexture("_detail", texture)];
    }

    public static void Write(StreamWriter writer, Material material, Func<string, string> texturePath)
    {
        if (LayerFor(material) is not { } layer)
        {
            return;
        }

        writer.WriteLine(FormattableString.Invariant($"    \"$detail\" \"{texturePath("_detail")}\""));
        writer.WriteLine(FormattableString.Invariant(
            $"    \"$detailblendmode\" \"{(layer.Blend == MaterialLayerBlend.Add ? _blendModeAdditive : _blendModeMod2X)}\""));
        writer.WriteLine("    \"$detailscale\" \"1\"");
    }
}
