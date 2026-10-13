using GMConverter.UnrealEngine.Materials;

namespace GMConverter.UnrealEngine.Profiles;

/// <summary>
/// How a game's master materials name and pack their textures, for the explorer to pick each
/// material's diffuse, normal and mask textures out of its parameters.
/// </summary>
/// <param name="ChannelAliases">
/// Per canonical channel (<c>Diffuse</c>, <c>Normals</c>, <c>SpecularMasks</c>), the material
/// parameters that hold it, most preferred first. A match replaces whatever the generic aliases chose.
/// </param>
/// <param name="MaskPacking">How a mask texture found through <paramref name="ChannelAliases"/> packs its channels, or null for Unreal's SpecularMasks layout.</param>
internal sealed record UnrealMaterialConventions(
    IReadOnlyList<(string Channel, string[] Aliases)> ChannelAliases,
    UnrealMaskPacking? MaskPacking = null)
{
    public static UnrealMaterialConventions None { get; } = new([]);
}
