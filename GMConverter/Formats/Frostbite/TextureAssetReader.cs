using System.Buffers.Binary;
using System.Text;
using GMConverter.Common;

namespace GMConverter.Formats.Frostbite;

// Parses Frostbite Texture RES blobs (ResType 0x6BDE20BA) for the SWS variant. The header is
// 0x88 bytes for Squadrons (trailing Unknown3[0] uint present) vs 0x84 for SWBF II (without).
// All multibyte fields are little-endian. Source: FrostyToolsuite 1.0.6.3
// FrostySdk/Resources/Texture.cs (L126-187).
internal static class TextureAssetReader
{
    public static FrostbiteTextureAsset Parse(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 0x84)
        {
            throw new GMConverterException(
                $"TextureAsset RES blob is too small ({blob.Length} bytes) to contain even the minimum SWBF II header (132 bytes).");
        }

        // mipOffsets[0..1] at 0x00..0x08 are streaming-skip cursors; we use MipSizes[] for actual
        // per-mip byte lengths so we can skip storing them.
        var type = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(0x08, 4));
        var pixelFormat = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(0x0C, 4));
        // 0x10..0x14 unknown1 (present for SWS + SWBF II).
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(0x14, 2));
        var width = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(0x16, 2));
        var height = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(0x18, 2));
        var depth = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(0x1A, 2));
        var sliceCount = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(0x1C, 2));
        var mipCount = blob[0x1E];
        var firstMip = blob[0x1F];
        var chunkId = new Guid(blob.Slice(0x20, 16));

        // MipSizes[15] at 0x30..0x6C. Index 0 = highest-resolution mip's byte count.
        var mipSizes = new uint[15];
        for (var i = 0; i < 15; i++)
        {
            mipSizes[i] = BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(0x30 + i * 4, 4));
        }

        var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(0x6C, 4));
        // 0x70..0x74 assetNameHash, 0x74..0x84 TextureGroup (16-byte null-padded fixed string).
        var textureGroup = ReadFixedString(blob.Slice(0x74, 16));

        return new FrostbiteTextureAsset(
            Width: width,
            Height: height,
            Depth: depth,
            SliceCount: sliceCount,
            MipCount: mipCount,
            FirstMip: firstMip,
            Type: type,
            PixelFormatOrdinal: pixelFormat,
            Flags: flags,
            ChunkId: chunkId,
            ChunkSize: chunkSize,
            MipSizes: mipSizes,
            TextureGroup: textureGroup);
    }

    private static string ReadFixedString(ReadOnlySpan<byte> field)
    {
        var end = 0;
        while (end < field.Length && field[end] != 0)
        {
            end++;
        }

        return end == 0 ? string.Empty : Encoding.UTF8.GetString(field[..end]);
    }
}
