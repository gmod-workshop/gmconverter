namespace GMConverter.Formats.Frostbite;

// Authoritative mapping of Frostbite's RenderFormat enum (SWBF II / Star Wars: Squadrons SDK)
// to BCn / uncompressed variants we can decode. Ordinal values dumped from the wavebend
// FrostyToolsuite fork's compiled StarWarsIISDK.dll via MetadataLoadContext — they are the
// stable IDs the engine writes into TextureAsset RES blobs for this profile cluster.
internal enum FrostbiteTextureFormat
{
    Unknown = 0,
    Bc1Unorm = 54,
    Bc1Srgb = 55,
    Bc1AUnorm = 56,
    Bc1ASrgb = 57,
    Bc2Unorm = 58,
    Bc2Srgb = 59,
    Bc3Unorm = 60,
    Bc3Srgb = 61,
    Bc4Unorm = 62,
    Bc5Unorm = 63,
    Bc6UFloat = 64,
    Bc6SFloat = 65,
    Bc7Unorm = 66,
    Bc7Srgb = 67,
    R8Unorm = 6,
    R8g8b8a8Unorm = 18,
    R8g8b8a8Srgb = 20,
    R16Unorm = 31,
    R16g16b16a16Float = 40,
    R16g16b16a16Unorm = 41,
    R32g32b32a32Float = 51,
    D16Unorm = 123
}

internal static class FrostbiteTextureFormatResolver
{
    public static FrostbiteTextureFormat Resolve(int ordinal)
    {
        // Cast through the enum so ordinals we haven't seen still come back as Unknown rather
        // than silently mismapping. Any new ordinal observed in the wild should be added above.
        return Enum.IsDefined(typeof(FrostbiteTextureFormat), ordinal)
            ? (FrostbiteTextureFormat)ordinal
            : FrostbiteTextureFormat.Unknown;
    }

    // Returns whether the format encodes pixel data we render visually. Depth-buffer formats
    // (D16/D24/D32) are surfaced by the asset enumerator but aren't useful as a diffuse/normal
    // binding, so callers can skip them upfront.
    public static bool IsRenderable(FrostbiteTextureFormat format)
    {
        return format != FrostbiteTextureFormat.Unknown && format != FrostbiteTextureFormat.D16Unorm;
    }
}
