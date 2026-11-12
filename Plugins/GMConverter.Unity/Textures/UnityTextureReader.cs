using System.Buffers.Binary;
using AssetRipper.TextureDecoder.Bc;
using AssetRipper.TextureDecoder.Rgb.Formats;
using GMConverter.Unity.Assets;
using Texture2DDecoder;

namespace GMConverter.Unity.Textures;

/// <summary>
/// Decodes the top mip level of a <c>Texture2D</c> object into RGBA. Unity stores rows from the
/// bottom of the image up; the result is flipped the right way up.
/// </summary>
internal static class UnityTextureReader
{
    /// <summary>The decoded texture, or null when its format is not supported.</summary>
    public static UnityTextureImage? Read(UnityAssetLibrary library, UnityObject texture)
    {
        var width = texture["m_Width"].AsInt;
        var height = texture["m_Height"].AsInt;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var format = (UnityTextureFormat)texture["m_TextureFormat"].AsInt;
        var data = library.ReadStreamedData(texture["m_StreamData"], texture["image data"].AsByteArray);
        var blocks = Uncrunch(data, format, texture.File.file.Metadata.UnityVersion);
        var rgba = blocks is { Length: > 0 } ? Decode(blocks, width, height, format) : null;
        if (rgba is null)
        {
            return null;
        }

        FlipRows(rgba, width, height);
        return new UnityTextureImage(texture.Name, width, height, rgba);
    }

    private static byte[]? Decode(byte[] data, int width, int height, UnityTextureFormat format)
    {
        byte[] output;
        switch (format)
        {
            case UnityTextureFormat.DXT1:
            case UnityTextureFormat.DXT1Crunched:
                Bc1.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output);
                return output;
            case UnityTextureFormat.DXT5:
            case UnityTextureFormat.DXT5Crunched:
                Bc3.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output);
                return output;
            case UnityTextureFormat.BC4:
                Bc4.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output);
                return output;
            case UnityTextureFormat.BC5:
                Bc5.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output);
                return output;
            case UnityTextureFormat.BC6H:
                Bc6h.Decompress<ColorRGBA<byte>, byte>(data, width, height, false, out output);
                return output;
            case UnityTextureFormat.BC7:
                Bc7.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output);
                return output;
            default:
                return DecodeUncompressed(data, width, height, format);
        }
    }

    // Crunch packs DXT blocks further; unpacking gives back the top level's DXT data. Unity 2017.3
    // changed the format to its own variant of Crunch.
    private static byte[]? Uncrunch(byte[] data, UnityTextureFormat format, string unityVersion)
    {
        if (format is not (UnityTextureFormat.DXT1Crunched or UnityTextureFormat.DXT5Crunched))
        {
            return data;
        }

        return UsesUnityCrunch(unityVersion) ? TextureDecoder.UnpackUnityCrunch(data) : TextureDecoder.UnpackCrunch(data);
    }

    internal static bool UsesUnityCrunch(string unityVersion)
    {
        var parts = unityVersion.Split('.');
        return parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor) ||
            major > 2017 || (major == 2017 && minor >= 3);
    }

    private static byte[]? DecodeUncompressed(byte[] data, int width, int height, UnityTextureFormat format)
    {
        var pixelCount = width * height;
        var bytesPerPixel = format switch
        {
            UnityTextureFormat.Alpha8 or UnityTextureFormat.R8 => 1,
            UnityTextureFormat.ARGB4444 or UnityTextureFormat.RGBA4444 or UnityTextureFormat.RGB565 or
                UnityTextureFormat.R16 or UnityTextureFormat.RG16 or UnityTextureFormat.RHalf => 2,
            UnityTextureFormat.RGB24 => 3,
            UnityTextureFormat.RGBA32 or UnityTextureFormat.ARGB32 or UnityTextureFormat.BGRA32 or
                UnityTextureFormat.RGHalf or UnityTextureFormat.RFloat => 4,
            UnityTextureFormat.RGBAHalf or UnityTextureFormat.RGFloat => 8,
            UnityTextureFormat.RGBAFloat => 16,
            _ => 0,
        };
        if (bytesPerPixel == 0 || data.Length < pixelCount * bytesPerPixel)
        {
            return null;
        }

        var rgba = new byte[pixelCount * 4];
        for (var pixel = 0; pixel < pixelCount; pixel++)
        {
            var source = data.AsSpan(pixel * bytesPerPixel, bytesPerPixel);
            var (r, g, b, a) = format switch
            {
                UnityTextureFormat.Alpha8 => ((byte)255, (byte)255, (byte)255, source[0]),
                UnityTextureFormat.R8 => (source[0], source[0], source[0], (byte)255),
                UnityTextureFormat.RG16 => (source[0], source[1], (byte)0, (byte)255),
                UnityTextureFormat.R16 => Grey(BinaryPrimitives.ReadUInt16LittleEndian(source) / 65535f),
                UnityTextureFormat.RHalf => Grey((float)BinaryPrimitives.ReadHalfLittleEndian(source)),
                UnityTextureFormat.RFloat => Grey(BinaryPrimitives.ReadSingleLittleEndian(source)),
                UnityTextureFormat.RGB24 => (source[0], source[1], source[2], (byte)255),
                UnityTextureFormat.RGBA32 => (source[0], source[1], source[2], source[3]),
                UnityTextureFormat.ARGB32 => (source[1], source[2], source[3], source[0]),
                UnityTextureFormat.BGRA32 => (source[2], source[1], source[0], source[3]),
                UnityTextureFormat.RGB565 => Rgb565(BinaryPrimitives.ReadUInt16LittleEndian(source)),
                UnityTextureFormat.ARGB4444 => Nibbles(BinaryPrimitives.ReadUInt16LittleEndian(source), argb: true),
                UnityTextureFormat.RGBA4444 => Nibbles(BinaryPrimitives.ReadUInt16LittleEndian(source), argb: false),
                UnityTextureFormat.RGHalf => (Unit((float)BinaryPrimitives.ReadHalfLittleEndian(source)), Unit((float)BinaryPrimitives.ReadHalfLittleEndian(source[2..])), (byte)0, (byte)255),
                UnityTextureFormat.RGFloat => (Unit(BinaryPrimitives.ReadSingleLittleEndian(source)), Unit(BinaryPrimitives.ReadSingleLittleEndian(source[4..])), (byte)0, (byte)255),
                UnityTextureFormat.RGBAHalf => (
                    Unit((float)BinaryPrimitives.ReadHalfLittleEndian(source)),
                    Unit((float)BinaryPrimitives.ReadHalfLittleEndian(source[2..])),
                    Unit((float)BinaryPrimitives.ReadHalfLittleEndian(source[4..])),
                    Unit((float)BinaryPrimitives.ReadHalfLittleEndian(source[6..]))),
                UnityTextureFormat.RGBAFloat => (
                    Unit(BinaryPrimitives.ReadSingleLittleEndian(source)),
                    Unit(BinaryPrimitives.ReadSingleLittleEndian(source[4..])),
                    Unit(BinaryPrimitives.ReadSingleLittleEndian(source[8..])),
                    Unit(BinaryPrimitives.ReadSingleLittleEndian(source[12..]))),
                _ => ((byte)0, (byte)0, (byte)0, (byte)0),
            };

            rgba[pixel * 4] = r;
            rgba[pixel * 4 + 1] = g;
            rgba[pixel * 4 + 2] = b;
            rgba[pixel * 4 + 3] = a;
        }

        return rgba;
    }

    private static (byte R, byte G, byte B, byte A) Grey(float value)
    {
        var grey = Unit(value);
        return (grey, grey, grey, 255);
    }

    private static (byte R, byte G, byte B, byte A) Rgb565(ushort value)
    {
        return ((byte)((value >> 11) * 255 / 31), (byte)(((value >> 5) & 0x3F) * 255 / 63), (byte)((value & 0x1F) * 255 / 31), 255);
    }

    private static (byte R, byte G, byte B, byte A) Nibbles(ushort value, bool argb)
    {
        var n0 = (byte)(((value >> 12) & 0xF) * 17);
        var n1 = (byte)(((value >> 8) & 0xF) * 17);
        var n2 = (byte)(((value >> 4) & 0xF) * 17);
        var n3 = (byte)((value & 0xF) * 17);
        return argb ? (n1, n2, n3, n0) : (n0, n1, n2, n3);
    }

    private static byte Unit(float value)
    {
        return (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
    }

    private static void FlipRows(byte[] rgba, int width, int height)
    {
        var stride = width * 4;
        var row = new byte[stride];
        for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
        {
            var topRow = rgba.AsSpan(top * stride, stride);
            var bottomRow = rgba.AsSpan(bottom * stride, stride);
            topRow.CopyTo(row);
            bottomRow.CopyTo(topRow);
            row.CopyTo(bottomRow);
        }
    }
}
