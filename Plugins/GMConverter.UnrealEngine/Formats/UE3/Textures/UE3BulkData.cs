using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Textures;

/// <summary>
/// A UE3 bulk data block. It is stored inline (at <see cref="InlineOffset"/> in the package data)
/// or, when <see cref="IsExternal"/>, in a texture cache at <see cref="OffsetInFile"/>.
/// </summary>
internal sealed record UE3BulkData(uint Flags, int ElementCount, int SizeOnDisk, long OffsetInFile, int InlineOffset)
{
    private const uint _storeInSeparateFile = 0x01;
    private const uint _unused = 0x20;

    public bool IsExternal => (Flags & _storeInSeparateFile) != 0;

    public bool IsEmpty => (Flags & _unused) != 0 || ElementCount == 0;

    public UE3CompressionMethod Compression => UE3Compression.FromBulkDataFlags(Flags);

    public static UE3BulkData Read(UE3Archive archive)
    {
        var flags = archive.ReadUInt32();
        var elementCount = archive.ReadInt32();
        var sizeOnDisk = archive.ReadInt32();
        var offsetInFile = archive.ReadInt32();
        var inlineOffset = archive.Position;
        if ((flags & _storeInSeparateFile) == 0 && sizeOnDisk > 0)
        {
            archive.Skip(sizeOnDisk);
        }

        return new UE3BulkData(flags, elementCount, sizeOnDisk, offsetInFile, inlineOffset);
    }
}
