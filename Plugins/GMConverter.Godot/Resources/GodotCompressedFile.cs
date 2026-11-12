using System.IO.Compression;
using GMConverter.SDK.Common;
using ZstdSharp;

namespace GMConverter.Godot.Resources;

/// <summary>
/// Undoes Godot's block compression (<c>FileAccessCompressed</c>), which exported scenes use: a
/// magic, the mode, the block size, the uncompressed size, each block's compressed size, then the
/// blocks.
/// </summary>
internal static class GodotCompressedFile
{
    private enum Mode
    {
        FastLz = 0,
        Deflate = 1,
        Zstd = 2,
        Gzip = 3,
    }

    /// <summary>Decompresses <paramref name="data"/>, whose four-byte magic has already been checked.</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        var mode = (Mode)BitConverter.ToInt32(data[4..]);
        var blockSize = BitConverter.ToInt32(data[8..]);
        var total = BitConverter.ToInt32(data[12..]);
        if (blockSize <= 0 || total < 0)
        {
            throw new GMConverterException("Godot compressed file has an invalid header.");
        }

        var blockCount = (total / blockSize) + 1;
        var offset = 16 + (blockCount * 4);
        var output = new byte[total];
        using var zstd = mode == Mode.Zstd ? new Decompressor() : null;

        for (var block = 0; block < blockCount; block++)
        {
            var compressedSize = BitConverter.ToInt32(data[(16 + (block * 4))..]);
            var start = block * blockSize;
            var length = Math.Min(blockSize, total - start);
            var compressed = data.Slice(offset, compressedSize);
            offset += compressedSize;
            if (length == 0)
            {
                continue;
            }

            var written = mode switch
            {
                Mode.Zstd => zstd!.Unwrap(compressed, output.AsSpan(start, length)),
                Mode.Deflate => Inflate(new ZLibStream(new MemoryStream(compressed.ToArray()), CompressionMode.Decompress), output.AsSpan(start, length)),
                Mode.Gzip => Inflate(new GZipStream(new MemoryStream(compressed.ToArray()), CompressionMode.Decompress), output.AsSpan(start, length)),
                _ => throw new GMConverterException($"Godot compression mode {mode} is not supported."),
            };
            if (written != length)
            {
                throw new GMConverterException("Godot compressed file is truncated.");
            }
        }

        return output;
    }

    private static int Inflate(Stream stream, Span<byte> output)
    {
        using (stream)
        {
            stream.ReadExactly(output);
            return output.Length;
        }
    }
}
