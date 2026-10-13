using GMConverter.SDK.Textures;

namespace GMConverter.UnrealEngine.Materials;

/// <summary>Repacks game-specific mask textures into Unreal's SpecularMasks layout, which every exporter reads.</summary>
internal static class UnrealMaskTextures
{
    // Unreal's default specular, 0.5: about 4% reflectance at normal incidence for non-metals.
    private const byte _defaultSpecular = 128;

    /// <summary>
    /// A <see cref="UnrealMaskPacking.MetallicRoughness"/> mask (R = metallic, G = roughness) as
    /// SpecularMasks (R = specular, G = metallic, B = roughness).
    /// </summary>
    public static Texture ToSpecularMasks(Texture metallicRoughness, ITextureFactory factory)
    {
        var source = metallicRoughness.GetRgbaPixels();
        var output = new byte[source.Length];
        for (var i = 0; i + 4 <= source.Length; i += 4)
        {
            output[i] = _defaultSpecular;
            output[i + 1] = source[i];
            output[i + 2] = source[i + 1];
            output[i + 3] = byte.MaxValue;
        }

        return factory.FromRgba($"{metallicRoughness.Name}_srm", metallicRoughness.Width, metallicRoughness.Height, output, hasAlpha: false, metallicRoughness.Path);
    }
}
