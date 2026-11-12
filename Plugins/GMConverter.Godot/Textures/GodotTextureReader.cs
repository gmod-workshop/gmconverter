using System.Buffers.Binary;
using AssetRipper.TextureDecoder.Bc;
using AssetRipper.TextureDecoder.Rgb.Formats;
using GMConverter.SDK.Textures;

namespace GMConverter.Godot.Textures;

/// <summary>
/// Reads Godot 4 compressed textures (<c>.ctex</c>, magic <c>GST2</c>): a header, then the image as
/// PNG or WebP files, one per mipmap, or as raw (often block-compressed) data. Only the full-size
/// image is decoded.
/// </summary>
internal static class GodotTextureReader
{
    private const int _imageOffset = 36;

    private enum DataFormat
    {
        Image = 0,
        Png = 1,
        WebP = 2,
        BasisUniversal = 3,
    }

    /// <summary>Whether <paramref name="data"/> is a Godot 4 compressed texture.</summary>
    public static bool IsTexture(ReadOnlySpan<byte> data)
    {
        return data.Length >= _imageOffset + 16 && data[..4].SequenceEqual("GST2"u8);
    }

    /// <summary>The decoded image, or null when its format cannot be decoded.</summary>
    public static GodotTextureImage? Read(byte[] data, string name, ITextureFactory textureFactory)
    {
        if (!IsTexture(data))
        {
            return null;
        }

        var span = data.AsSpan(_imageOffset);
        var dataFormat = (DataFormat)BinaryPrimitives.ReadUInt32LittleEndian(span);
        var width = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(span[6..]);
        var format = (GodotImageFormat)BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
        var payload = span[16..];

        switch (dataFormat)
        {
            case DataFormat.Png:
            case DataFormat.WebP:
                {
                    // Each mipmap is a file of its own, after its size; the first is the full image.
                    var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload);
                    using var stream = new MemoryStream(payload.Slice(4, size).ToArray());
                    using var texture = textureFactory.FromStream(name, stream);
                    return new GodotTextureImage(name, texture.Width, texture.Height, texture.GetRgbaPixels());
                }

            case DataFormat.Image:
                return Decode(payload.ToArray(), width, height, format) is { } rgba
                    ? new GodotTextureImage(name, width, height, rgba, format is GodotImageFormat.RgtcRg or GodotImageFormat.Dxt5RaAsRg)
                    : null;

            default:
                return null;
        }
    }

    private static byte[]? Decode(byte[] data, int width, int height, GodotImageFormat format)
    {
        byte[] output;
        switch (format)
        {
            case GodotImageFormat.Dxt1:
                Bc1.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output);
                return output;
            case GodotImageFormat.Dxt3:
                Bc2.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output);
                return output;
            case GodotImageFormat.Dxt5:
                Bc3.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output);
                return output;
            case GodotImageFormat.Dxt5RaAsRg:
                Bc3.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output);
                for (var i = 0; i < output.Length; i += 4)
                {
                    output[i + 1] = output[i + 3];
                    output[i + 2] = 255;
                    output[i + 3] = 255;
                }

                return output;
            case GodotImageFormat.RgtcR:
                Bc4.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output);
                return output;
            case GodotImageFormat.RgtcRg:
                Bc5.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output);
                return output;
            case GodotImageFormat.BptcRgba:
                Bc7.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output);
                return output;
            case GodotImageFormat.BptcRgbf:
            case GodotImageFormat.BptcRgbfu:
                Bc6h.Decompress<ColorRGBA<byte>, byte>(data, width, height, format == GodotImageFormat.BptcRgbf, out output);
                return output;
            default:
                return DecodeUncompressed(data, width, height, format);
        }
    }

    private static byte[]? DecodeUncompressed(byte[] data, int width, int height, GodotImageFormat format)
    {
        var channels = format switch
        {
            GodotImageFormat.L8 or GodotImageFormat.R8 => 1,
            GodotImageFormat.La8 or GodotImageFormat.Rg8 => 2,
            GodotImageFormat.Rgb8 => 3,
            GodotImageFormat.Rgba8 => 4,
            _ => 0,
        };
        var count = width * height;
        if (channels == 0 || data.Length < count * channels)
        {
            return null;
        }

        var output = new byte[count * 4];
        for (var pixel = 0; pixel < count; pixel++)
        {
            var source = data.AsSpan(pixel * channels, channels);
            var target = output.AsSpan(pixel * 4, 4);
            switch (format)
            {
                case GodotImageFormat.L8:
                    target[0] = target[1] = target[2] = source[0];
                    target[3] = 255;
                    break;
                case GodotImageFormat.La8:
                    target[0] = target[1] = target[2] = source[0];
                    target[3] = source[1];
                    break;
                case GodotImageFormat.R8:
                    target[0] = source[0];
                    target[3] = 255;
                    break;
                case GodotImageFormat.Rg8:
                    target[0] = source[0];
                    target[1] = source[1];
                    target[3] = 255;
                    break;
                case GodotImageFormat.Rgb8:
                    source.CopyTo(target);
                    target[3] = 255;
                    break;
                default:
                    source.CopyTo(target);
                    break;
            }
        }

        return output;
    }
}
