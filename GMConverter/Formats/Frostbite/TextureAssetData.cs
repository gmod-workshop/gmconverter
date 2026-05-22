namespace GMConverter.Formats.Frostbite;

// Parsed shape of a Frostbite TextureAsset RES blob. The pixel-format field is the raw
// per-game RenderFormat ordinal (decoded by TypeLibrary at runtime in Frosty — we don't have
// that, so callers infer the BCn variant from mip0 byte size). Mip 0 is the highest-resolution
// image; subsequent entries in MipSizes shrink by powers of two.
internal sealed record FrostbiteTextureAsset(
    int Width,
    int Height,
    int Depth,
    int SliceCount,
    int MipCount,
    int FirstMip,
    int Type,
    int PixelFormatOrdinal,
    ushort Flags,
    Guid ChunkId,
    uint ChunkSize,
    IReadOnlyList<uint> MipSizes,
    string TextureGroup)
{
    public bool IsSrgb => (Flags & 0x0002) != 0;
}
