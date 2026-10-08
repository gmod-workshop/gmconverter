namespace GMConverter.SDK.Materials;

/// <summary>How <see cref="Material.SpecularTexture"/> packs its channels.</summary>
public enum MaterialSpecularTexturePacking
{
    /// <summary>A plain specular map.</summary>
    Standard,

    /// <summary>
    /// R = specular intensity, G = metallic, B = roughness, as in Unreal Engine's SpecularMasks
    /// textures. Exporters derive their own maps (glTF metallic-roughness, Source phong) from it.
    /// </summary>
    SpecularMetallicRoughness
}
