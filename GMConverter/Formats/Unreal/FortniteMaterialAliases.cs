namespace GMConverter.Formats.Unreal;

/// <summary>
/// Promotes Fortnite-specific texture parameter names to canonical PBR channel slots
/// (Diffuse / Normals / SpecularMasks / Emission). Ports the texture aliases from
/// FortnitePorting's Blender plugin DefaultMappings + the most common master-material
/// specific tables (Trunk, PBW/Builder_2, Valet). Without this, our exporter has to fall
/// back to the unreliable CMaterialParams2 PM_* slots when a master material uses
/// non-standard parameter names — which is most Fortnite vehicle/playset content.
/// </summary>
internal static class FortniteMaterialAliases
{
    public const string DiffuseChannel = "Diffuse";
    public const string NormalsChannel = "Normals";
    public const string SpecularMasksChannel = "SpecularMasks";
    public const string EmissionChannel = "Emission";

    /// <summary>
    /// Each tuple is (canonical-channel, priority-ordered parameter aliases). When the same
    /// material exposes multiple aliases for a channel, the first match wins — so put the
    /// most specific / preferred names earlier.
    /// </summary>
    public static readonly (string Channel, string[] Aliases)[] DefaultChannelAliases =
    [
        (DiffuseChannel,
        [
            // Generic / canonical
            "Diffuse",
            "D",
            "Base Color", "BaseColor",
            "Diffuse Texture", "DiffuseTexture",
            "Diffuse_Texture",
            "BaseColorTexture", "BaseColor Map",
            "Diffuse Top",
            "___Diffuse",
            // Master-material specific (DefaultMappings + BaseTrunkMappings)
            "Trunk_BaseColor", "BaseColor_Trunk",
            "Concrete",
            "CliffTexture",
            // Multi-layer base
            "Background Diffuse",
            "BG Diffuse Texture",
            // Last-resort fallback from CMaterialParams2.VerifyTexture
            "PM_Diffuse",
        ]),

        (NormalsChannel,
        [
            "Normals",
            "N",
            "Normal", "NormalMap", "Normal Map",
            "NormalTexture", "Normal Texture",
            "Normals Top",
            "BakedNormal", "Baked Normal",
            "Trunk_Normal", "Normal_Trunk",
            "ConcreteTextureNormal",
            "CliffNormal",
            "_Normal",
            "PM_Normals",
        ]),

        (SpecularMasksChannel,
        [
            "SpecularMasks",
            "S",
            "SRM",
            "S Mask", "Specular Mask", "SpecularMask",
            "Input S",
            "Specular Top",
            "Trunk_Specular", "SMR_Trunk",
            "Concrete_SpecMask",
            "Cliff Spec Texture",
            "__PBR Masks",
            "MetallicRoughnessTexture",
            "Bake Packed Maps",
            "PM_SpecularMasks",
        ]),

        (EmissionChannel,
        [
            "Emission",
            "Emissive",
            "EmissiveColor",
            "EmissiveTexture",
            "L1_Emissive",
            "PM_Emissive",
        ]),
    ];
}
