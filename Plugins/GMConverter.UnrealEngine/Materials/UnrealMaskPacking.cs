namespace GMConverter.UnrealEngine.Materials;

/// <summary>How a game packs the channels of a material's mask texture.</summary>
internal enum UnrealMaskPacking
{
    /// <summary>Unreal's SpecularMasks layout: R = specular, G = metallic, B = roughness.</summary>
    SpecularMetallicRoughness,

    /// <summary>
    /// R = metallic, G = roughness, with no specular channel (Star Wars Zero Company). The importer
    /// repacks it as <see cref="SpecularMetallicRoughness"/>.
    /// </summary>
    MetallicRoughness,
}
