using System.Text;
using GMConverter.UnrealEngine.Formats.UE3.Packages;
using GMConverter.UnrealEngine.Formats.UE3.Textures;
using K4os.Compression.LZ4;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UE3PackageTests
{
    private const int _version = 872;
    private const int _licenseeVersion = 5;
    private static readonly string[] _names = ["None", "Core", "Engine", "Package", "Class", "Texture2D", "T_Knife_D", "SizeX", "IntProperty", "AddressX", "ByteProperty", "TextureAddress", "TA_Mirror", "SRGB", "BoolProperty"];

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReadsTablesAndPropertiesOfStockAndCrossFirePackages(bool crossFireNames, bool lz4)
    {
        var path = WritePackage(crossFireNames, lz4);

        var package = UE3Package.Read(path);

        Assert.True(UE3Package.IsPackage(path));
        Assert.Equal(_names, package.Names);
        var export = Assert.Single(package.Exports);
        Assert.Equal("T_Knife_D", export.ObjectName);
        Assert.Equal("Texture2D", package.GetClassName(export));
        Assert.Equal("Engine.Texture2D", package.GetPath(export.ClassIndex));
        var (_, properties) = package.OpenObject(export);
        Assert.Equal(64, properties.GetInt("SizeX"));
        Assert.Equal("TA_Mirror", properties.GetName("AddressX"));
        Assert.False(properties.GetBool("SRGB"));
    }

    [Fact]
    public void TextureAddressingComesFromTheTexturesOwnProperties()
    {
        var path = WritePackage(crossFireNames: true, lz4: true);
        var package = UE3Package.Read(path);

        var texture = UE3Texture2D.Read(package, package.Exports[0]);

        Assert.Equal(UE3TextureAddress.Mirror, texture.AddressX);
        Assert.Equal(UE3TextureAddress.Wrap, texture.AddressY);
        Assert.False(texture.Srgb);
        Assert.Equal(64, texture.Width);
        Assert.Empty(texture.Mips);
    }

    [Fact]
    public void CompressedChunksSpanSeveralBlocks()
    {
        var data = Enumerable.Range(0, 300_000).Select(index => (byte)(index % 251)).ToArray();
        using var stream = new MemoryStream(Chunk(data, blockSize: 0x20000));

        Assert.Equal(data, UE3Compression.ReadChunk(stream, 0, UE3CompressionMethod.Lz4));
        Assert.Equal(stream.Length, UE3Compression.GetStoredChunkSize(stream, 0));
    }

    // A package holding one Texture2D whose class is imported from Engine. CrossFire HD pads each
    // name entry, and LZ4-compresses everything after the summary as one chunk (flags 24).
    private static string WritePackage(bool crossFireNames, bool lz4)
    {
        int Name(string name) => Array.IndexOf(_names, name);

        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        void WriteName(string name)
        {
            writer.Write(Name(name));
            writer.Write(0);
        }

        // Tables are laid out from the body's start; the summary in front is added afterwards.
        var summarySize = SummarySize(lz4 ? 1 : 0);
        var nameOffset = summarySize;
        foreach (var name in _names)
        {
            writer.Write(name.Length + 1);
            writer.Write(Encoding.ASCII.GetBytes(name));
            writer.Write((byte)0);
            if (crossFireNames)
            {
                writer.Write(new byte[(name.Length + 4) & 7]);
            }

            writer.Write(0x0007001000000000UL);
        }

        var importOffset = summarySize + (int)body.Position;
        WriteName("Core");
        WriteName("Package");
        writer.Write(0);
        WriteName("Engine");
        WriteName("Core");
        WriteName("Class");
        writer.Write(-1);
        WriteName("Texture2D");

        var exportOffset = summarySize + (int)body.Position;
        var exportEntry = body.Position;
        writer.Write(new byte[4 * 3 + 8 + 4 + 8 + 4 + 4 + 4 + 4 + 16 + 4]);

        var serialOffset = summarySize + (int)body.Position;
        writer.Write(0); // net index
        WriteName("SizeX");
        WriteName("IntProperty");
        writer.Write(4);
        writer.Write(0);
        writer.Write(64);
        WriteName("AddressX");
        WriteName("ByteProperty");
        writer.Write(8);
        writer.Write(0);
        WriteName("TextureAddress");
        WriteName("TA_Mirror");
        WriteName("SRGB");
        WriteName("BoolProperty");
        writer.Write(0);
        writer.Write(0);
        writer.Write((byte)0);
        WriteName("None");
        writer.Write(new byte[16]); // empty source art
        writer.Write(0); // no mips
        var serialSize = summarySize + (int)body.Position - serialOffset;

        body.Position = exportEntry;
        writer.Write(-2); // class
        writer.Write(0); // super
        writer.Write(0); // outer
        WriteName("T_Knife_D");
        writer.Write(0); // archetype
        writer.Write(0UL); // flags
        writer.Write(serialSize);
        writer.Write(serialOffset);
        var bodyBytes = body.ToArray();

        using var file = new MemoryStream();
        using var summary = new BinaryWriter(file);
        summary.Write(UE3Compression.PackageTag);
        summary.Write((ushort)_version);
        summary.Write((ushort)_licenseeVersion);
        summary.Write(serialOffset); // header size: the tables end where the object data starts
        summary.Write(5);
        summary.Write(Encoding.ASCII.GetBytes("None\0"));
        summary.Write(0u);
        summary.Write(_names.Length);
        summary.Write(nameOffset);
        summary.Write(1);
        summary.Write(exportOffset);
        summary.Write(2);
        summary.Write(importOffset);
        summary.Write(serialOffset); // depends
        summary.Write(new byte[4 * 4]); // guid tables, thumbnails
        summary.Write(new byte[16]);
        summary.Write(0); // generations
        summary.Write(10897);
        summary.Write(0);
        if (!lz4)
        {
            summary.Write(0u);
            summary.Write(0);
            summary.Write(bodyBytes);
        }
        else
        {
            summary.Write(0x18u);
            summary.Write(1);
            summary.Write(summarySize);
            summary.Write(bodyBytes.Length);
            summary.Write(summarySize);
            var chunk = Chunk(bodyBytes, blockSize: 0x20000);
            summary.Write(chunk.Length);
            summary.Write(chunk);
        }

        var path = Path.Join(PskFixtures.CreateDirectory(), "package");
        File.WriteAllBytes(path, file.ToArray());
        return path;
    }

    private static int SummarySize(int chunkCount) => 4 + 4 + 4 + 4 + 5 + 4 + 4 * 7 + 4 * 4 + 16 + 4 + 4 + 4 + 4 + 4 + chunkCount * 16;

    private static byte[] Chunk(byte[] data, int blockSize)
    {
        List<(byte[] Compressed, int Size)> blocks = [];
        for (var offset = 0; offset < data.Length; offset += blockSize)
        {
            var size = Math.Min(blockSize, data.Length - offset);
            var target = new byte[LZ4Codec.MaximumOutputSize(size)];
            var written = LZ4Codec.Encode(data.AsSpan(offset, size), target);
            blocks.Add((target[..written], size));
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(UE3Compression.PackageTag);
        writer.Write(blockSize);
        writer.Write(blocks.Sum(block => block.Compressed.Length));
        writer.Write(data.Length);
        foreach (var (compressed, size) in blocks)
        {
            writer.Write(compressed.Length);
            writer.Write(size);
        }

        foreach (var (compressed, _) in blocks)
        {
            writer.Write(compressed);
        }

        return stream.ToArray();
    }
}
