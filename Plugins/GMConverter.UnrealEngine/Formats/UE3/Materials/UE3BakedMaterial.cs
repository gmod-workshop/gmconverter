using GMConverter.UnrealEngine.Formats.UE3.Textures;
using GMConverter.UnrealEngine.Materials;

namespace GMConverter.UnrealEngine.Formats.UE3.Materials;

/// <summary>
/// A UE3 material baked to plain textures: sRGB colour (with opacity in alpha when the material
/// is masked or translucent), a tangent-space normal map, and a specular mask packed as
/// R = specular, G = metallic, B = roughness. <c>AddressX</c>/<c>AddressY</c> are how the
/// material's base texture repeats, which the mesh UVs need when it mirrors.
/// </summary>
internal sealed record UE3BakedMaterial(
    string Name,
    DecodedImage? Diffuse,
    DecodedImage? Normal,
    DecodedImage? SpecularMasks,
    DecodedImage? Emissive,
    string? Blend,
    float AlphaCutoff,
    UE3TextureAddress AddressX,
    UE3TextureAddress AddressY)
{
    /// <summary>For a particle material baked in texture space, the slide a particle parameter drives.</summary>
    public UE3ParticleUvAnimation? ParticleUv { get; init; }
}
