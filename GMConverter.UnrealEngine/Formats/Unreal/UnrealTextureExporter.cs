using System.Text;
using CUE4Parse_Conversion.Textures.DXT;
using GMConverter.SDK.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GMConverter.UnrealEngine.Formats.Unreal;

internal static class UnrealTextureExporter
{
    private const int _texfDxt1 = 3;
    private const int _texfDxt3 = 7;
    private const int _texfDxt5 = 8;

    public static UnrealExportedTexture? ExportTexture(UnrealResolvedObject texture, string outputDirectory)
    {
        if (texture.Package is null || texture.Export is null)
        {
            return null;
        }

        var textureName = NameHelpers.SanitizeMaterialName(texture.ObjectName);
        // PNG rather than DDS: the importers decode textures with ImageSharp, which has no DDS
        // support, so a DDS sidecar left every UE2 Explorer material untextured or failing.
        var outputPath = Path.Combine(outputDirectory, textureName + ".png");
        using var stream = File.OpenRead(texture.Package.FilePath);
        using var binaryReader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var reader = new UnrealObjectReader(texture.Package, binaryReader, texture.Export);
        var properties = reader.ReadProperties();
        var format = properties.FirstInteger("Format") ?? properties.FirstInteger("CompFormat");
        if (format is null)
        {
            return null;
        }

        IReadOnlyList<UnrealTextureMip> mips;
        try
        {
            mips = reader.ReadArray(() => ReadMip(reader))
                .Where(mip => mip.Data.Count > 0 && mip.Width > 0 && mip.Height > 0)
                .ToArray();
        }
        catch (GMConverterException)
        {
            return null;
        }

        if (mips.Count == 0)
        {
            return null;
        }

        var topMip = mips[0];
        var rgba = DecodeDxt(format.Value, topMip);
        if (rgba is null)
        {
            return null;
        }

        if (!File.Exists(outputPath))
        {
            Directory.CreateDirectory(outputDirectory);
            using var image = Image.LoadPixelData<Rgba32>(rgba, topMip.Width, topMip.Height);
            image.SaveAsPng(outputPath);
        }

        return new UnrealExportedTexture(textureName, TextureFormatHasAlpha(format.Value));
    }

    private static UnrealTextureMip ReadMip(UnrealObjectReader reader)
    {
        var data = reader.ReadLazyArray(reader.ReadByte);
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        _ = reader.ReadByte();
        _ = reader.ReadByte();
        return new UnrealTextureMip(data, width, height);
    }

    // Returns tightly packed RGBA8 for the mip, or null for formats the exporter doesn't handle.
    private static byte[]? DecodeDxt(int textureFormat, UnrealTextureMip mip)
    {
        var data = mip.Data.ToArray();
        var requiredSize = CalculateDxtMipSize(mip.Width, mip.Height, textureFormat == _texfDxt1 ? 8 : 16);
        if (data.Length < requiredSize)
        {
            return null;
        }

        return textureFormat switch
        {
            _texfDxt1 => DXTDecoder.DXT1(data, mip.Width, mip.Height, 1),
            _texfDxt3 => DecodeDxt3(data, mip.Width, mip.Height),
            _texfDxt5 => DXTDecoder.DXT5(data, mip.Width, mip.Height, 1),
            _ => null
        };
    }

    // DXT3 pairs 4-bit explicit alpha with a color block that always uses four-color mode, as
    // DXT5's does. Repack each block as DXT5 with opaque interpolated alpha, decode the color with
    // the DXT5 decoder, then write the explicit alpha back.
    private static byte[] DecodeDxt3(byte[] data, int width, int height)
    {
        var blocksWide = Math.Max(1, (width + 3) / 4);
        var blocksHigh = Math.Max(1, (height + 3) / 4);
        var asDxt5 = new byte[blocksWide * blocksHigh * 16];
        for (var block = 0; block < blocksWide * blocksHigh; block++)
        {
            var offset = block * 16;
            asDxt5[offset] = byte.MaxValue;
            asDxt5[offset + 1] = byte.MaxValue;
            Array.Copy(data, offset + 8, asDxt5, offset + 8, 8);
        }

        var rgba = DXTDecoder.DXT5(asDxt5, width, height, 1);
        for (var blockY = 0; blockY < blocksHigh; blockY++)
        {
            for (var blockX = 0; blockX < blocksWide; blockX++)
            {
                var alphaBits = BitConverter.ToUInt64(data, ((blockY * blocksWide) + blockX) * 16);
                for (var texel = 0; texel < 16; texel++)
                {
                    var x = (blockX * 4) + (texel % 4);
                    var y = (blockY * 4) + (texel / 4);
                    if (x < width && y < height)
                    {
                        var alpha = (byte)((alphaBits >> (texel * 4)) & 0xF);
                        rgba[(((y * width) + x) * 4) + 3] = (byte)(alpha * 17);
                    }
                }
            }
        }

        return rgba;
    }

    private static bool TextureFormatHasAlpha(int textureFormat)
    {
        return textureFormat is _texfDxt3 or _texfDxt5;
    }

    private static int CalculateDxtMipSize(int width, int height, int blockSize)
    {
        return Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * blockSize;
    }

    private sealed record UnrealTextureMip(IReadOnlyList<byte> Data, int Width, int Height);
}

internal sealed record UnrealExportedTexture(string Name, bool HasAlpha);
