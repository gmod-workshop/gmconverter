using System.Buffers.Binary;
using System.IO.Compression;
using GMConverter.SDK.Common;
using K4os.Compression.LZ4;

namespace GMConverter.UnrealEngine.Formats.UE3.Packages;

/// <summary>
/// Reads UE3 compressed chunks: a header (tag, block size, total sizes), a table of per-block
/// sizes, then the blocks. Package bodies and texture mips stored in texture caches share it.
/// </summary>
internal static class UE3Compression
{
    public const uint PackageTag = 0x9E2A83C1;

    // Stock UE3 package flags, plus CrossFire HD's LZ4 (24).
    private const uint _packageZlib = 0x01;
    private const uint _packageLzo = 0x02;
    private const uint _packageLzx = 0x04;
    private const uint _crossFirePackageLz4 = 0x18;

    // Stock UE3 bulk data flags, plus CrossFire HD's LZ4 (0x100).
    private const uint _bulkZlib = 0x02;
    private const uint _bulkLzo = 0x10;
    private const uint _bulkLzx = 0x80;
    private const uint _crossFireBulkLz4 = 0x100;

    public static UE3CompressionMethod FromPackageFlags(uint flags)
    {
        return flags switch
        {
            0 => UE3CompressionMethod.None,
            _packageZlib => UE3CompressionMethod.Zlib,
            _packageLzo => UE3CompressionMethod.Lzo,
            _packageLzx => UE3CompressionMethod.Lzx,
            _crossFirePackageLz4 => UE3CompressionMethod.Lz4,
            _ => throw new GMConverterException($"Unsupported UE3 package compression flags 0x{flags:X}."),
        };
    }

    public static UE3CompressionMethod FromBulkDataFlags(uint flags)
    {
        if ((flags & _crossFireBulkLz4) != 0)
        {
            return UE3CompressionMethod.Lz4;
        }

        if ((flags & _bulkZlib) != 0)
        {
            return UE3CompressionMethod.Zlib;
        }

        if ((flags & _bulkLzo) != 0)
        {
            return UE3CompressionMethod.Lzo;
        }

        return (flags & _bulkLzx) != 0 ? UE3CompressionMethod.Lzx : UE3CompressionMethod.None;
    }

    /// <summary>Reads and decompresses the chunk starting at <paramref name="offset"/>.</summary>
    public static byte[] ReadChunk(Stream stream, long offset, UE3CompressionMethod method)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        Span<byte> header = stackalloc byte[16];
        stream.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != PackageTag)
        {
            throw new GMConverterException($"UE3 compressed chunk at 0x{offset:X} has no chunk tag.");
        }

        var blockSize = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        var uncompressedSize = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
        if (blockSize <= 0 || uncompressedSize < 0)
        {
            throw new GMConverterException($"UE3 compressed chunk at 0x{offset:X} has an invalid size.");
        }

        var blockCount = (int)(((long)uncompressedSize + blockSize - 1) / blockSize);
        var table = new byte[blockCount * 8];
        stream.ReadExactly(table);

        var output = new byte[uncompressedSize];
        var outputOffset = 0;
        for (var block = 0; block < blockCount; block++)
        {
            var compressed = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(block * 8));
            var uncompressed = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(block * 8 + 4));
            if (compressed < 0 || uncompressed < 0 || outputOffset + uncompressed > output.Length)
            {
                throw new GMConverterException($"UE3 compressed chunk at 0x{offset:X} has an invalid block table.");
            }

            var source = new byte[compressed];
            stream.ReadExactly(source);
            Decompress(method, source, output.AsSpan(outputOffset, uncompressed));
            outputOffset += uncompressed;
        }

        return output;
    }

    /// <summary>The bytes a chunk starting at <paramref name="offset"/> occupies, header included.</summary>
    public static long GetStoredChunkSize(Stream stream, long offset)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        Span<byte> header = stackalloc byte[16];
        if (stream.Read(header) < header.Length || BinaryPrimitives.ReadUInt32LittleEndian(header) != PackageTag)
        {
            return -1;
        }

        var blockSize = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        if (blockSize <= 0)
        {
            return -1;
        }

        var blockCount = (uncompressedSize + (long)blockSize - 1) / blockSize;
        return 16 + blockCount * 8 + compressedSize;
    }

    private static void Decompress(UE3CompressionMethod method, byte[] source, Span<byte> destination)
    {
        switch (method)
        {
            case UE3CompressionMethod.None:
                source.AsSpan(0, destination.Length).CopyTo(destination);
                return;
            case UE3CompressionMethod.Lz4:
                if (LZ4Codec.Decode(source, destination) != destination.Length)
                {
                    throw new GMConverterException("UE3 LZ4 block decompressed to the wrong size.");
                }

                return;
            case UE3CompressionMethod.Zlib:
                using (var zlib = new ZLibStream(new MemoryStream(source), CompressionMode.Decompress))
                {
                    zlib.ReadExactly(destination);
                }

                return;
            case UE3CompressionMethod.Lzo:
                UE3Lzo.Decompress(source, destination);
                return;
            default:
                throw new GMConverterException($"UE3 {method} compression is not supported.");
        }
    }
}
