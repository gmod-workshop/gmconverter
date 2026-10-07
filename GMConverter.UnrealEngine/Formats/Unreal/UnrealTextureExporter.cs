using System.Text;
using CUE4Parse_Conversion.Textures.DXT;
using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GMConverter.UnrealEngine.Formats.Unreal;

internal static class UnrealTextureExporter
{
    private const int _texfDxt1 = 3;
    private const int _texfRgba8 = 5;
    private const int _texfDxt3 = 7;
    private const int _texfDxt5 = 8;

    // Single-channel 16-bit grayscale. Republic Commando's effect gradients only use 15 bits:
    // every sampled texture tops out at exactly 32767, so that is full scale.
    private const int _texfG16 = 10;
    private const float _g16FullScale = 32767f;

    // Republic Commando format 12: a signed two-channel tangent-space normal map. A curl test on
    // Kaminoan_Bump showed byte 1 is X and byte 0 is Y with Y pointing up the image (OpenGL).
    private const int _texfRepublicCommandoNormal = 12;

    // Republic Commando stores shader Bumpmap textures in format 14 as two bytes per texel: a
    // height map (byte 1, closely tracking the diffuse luminance) and a sparse panel-edge mask
    // (byte 0). Stock UE2 tables call 14 TEXF_3DC, which is one byte per texel, so the payload
    // size tells the two apart.
    private const int _texfRepublicCommandoBump = 14;

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
        var rgba = DecodeMip(format.Value, topMip);
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
    // Paletted P8 is not decoded: in Republic Commando it only appears in font atlases and editor
    // labels, never on a mesh.
    private static byte[]? DecodeMip(int textureFormat, UnrealTextureMip mip)
    {
        var data = mip.Data.ToArray();
        return textureFormat switch
        {
            _texfDxt1 or _texfDxt3 or _texfDxt5 => DecodeDxt(textureFormat, data, mip.Width, mip.Height),
            _texfRgba8 => DecodeBgra8(data, mip.Width, mip.Height),
            _texfG16 => DecodeG16(data, mip.Width, mip.Height),
            _texfRepublicCommandoNormal => DecodeRepublicCommandoNormal(data, mip.Width, mip.Height),
            _texfRepublicCommandoBump => DecodeRepublicCommandoBump(data, mip.Width, mip.Height),
            _ => null
        };
    }

    private static byte[]? DecodeDxt(int textureFormat, byte[] data, int width, int height)
    {
        if (data.Length < CalculateDxtMipSize(width, height, textureFormat == _texfDxt1 ? 8 : 16))
        {
            return null;
        }

        return textureFormat switch
        {
            _texfDxt1 => DXTDecoder.DXT1(data, width, height, 1),
            _texfDxt3 => DecodeDxt3(data, width, height),
            _ => DXTDecoder.DXT5(data, width, height, 1)
        };
    }

    // TEXF_RGBA8 is stored in D3D order (B, G, R, A); verified on the HoloBlue/HoloRed gradients.
    private static byte[]? DecodeBgra8(byte[] data, int width, int height)
    {
        if (data.Length != width * height * 4)
        {
            return null;
        }

        for (var i = 0; i < data.Length; i += 4)
        {
            (data[i], data[i + 2]) = (data[i + 2], data[i]);
        }

        return data;
    }

    private static byte[]? DecodeG16(byte[] data, int width, int height)
    {
        var texelCount = width * height;
        if (data.Length != texelCount * 2)
        {
            return null;
        }

        var rgba = new byte[texelCount * 4];
        for (var i = 0; i < texelCount; i++)
        {
            var value = data[i * 2] | (data[(i * 2) + 1] << 8);
            var gray = (byte)Math.Min(255, (int)MathF.Round(value * 255f / _g16FullScale));
            rgba[i * 4] = gray;
            rgba[(i * 4) + 1] = gray;
            rgba[(i * 4) + 2] = gray;
            rgba[(i * 4) + 3] = byte.MaxValue;
        }

        return rgba;
    }

    private static byte[]? DecodeRepublicCommandoNormal(byte[] data, int width, int height)
    {
        var texelCount = width * height;
        if (data.Length != texelCount * 2)
        {
            return null;
        }

        static float Signed(byte value) => Math.Max(-1f, (sbyte)value / 127f);
        static byte Encode(float component) => (byte)Math.Clamp(MathF.Round(((component * 0.5f) + 0.5f) * 255f), 0f, 255f);

        var rgba = new byte[texelCount * 4];
        for (var i = 0; i < texelCount; i++)
        {
            var x = Signed(data[(i * 2) + 1]);
            var y = Signed(data[i * 2]);
            var z = MathF.Sqrt(Math.Max(0f, 1f - (x * x) - (y * y)));
            rgba[i * 4] = Encode(x);
            rgba[(i * 4) + 1] = Encode(y);
            rgba[(i * 4) + 2] = Encode(z);
            rgba[(i * 4) + 3] = byte.MaxValue;
        }

        return rgba;
    }

    private static byte[]? DecodeRepublicCommandoBump(byte[] data, int width, int height)
    {
        var texelCount = width * height;
        if (data.Length != texelCount * 2)
        {
            return null;
        }

        var heights = new byte[texelCount];
        for (var i = 0; i < texelCount; i++)
        {
            heights[i] = data[(i * 2) + 1];
        }

        return HeightNormalMap.FromHeights(heights, width, height);
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
        return textureFormat is _texfDxt3 or _texfDxt5 or _texfRgba8;
    }

    private static int CalculateDxtMipSize(int width, int height, int blockSize)
    {
        return Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * blockSize;
    }

    private sealed record UnrealTextureMip(IReadOnlyList<byte> Data, int Width, int Height);
}

internal sealed record UnrealExportedTexture(string Name, bool HasAlpha);
