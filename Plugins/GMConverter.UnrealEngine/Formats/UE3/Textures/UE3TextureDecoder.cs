using CUE4Parse_Conversion.Textures.BC;
using GMConverter.UnrealEngine.Formats.UE3.Packages;
using GMConverter.UnrealEngine.Materials;

namespace GMConverter.UnrealEngine.Formats.UE3.Textures;

/// <summary>Decodes a UE3 texture's largest available mip to RGBA.</summary>
internal static class UE3TextureDecoder
{
    public static DecodedImage Decode(UE3Package package, UE3Texture2D texture, UE3TextureCaches caches)
    {
        foreach (var mip in texture.Mips)
        {
            if (mip.Data.IsEmpty || mip.Width <= 0 || mip.Height <= 0 || ReadMip(package, texture, mip, caches) is not { } data)
            {
                continue;
            }

            if (DecodePixels(texture.Format, data, mip.Width, mip.Height) is { } rgba)
            {
                return new DecodedImage(rgba, mip.Width, mip.Height, DecodedImagePixelFormat.Rgba8888);
            }
        }

        return DecodedImage.Empty;
    }

    private static byte[]? ReadMip(UE3Package package, UE3Texture2D texture, UE3TextureMip mip, UE3TextureCaches caches)
    {
        if (mip.Data.IsExternal)
        {
            return caches.ReadExternalMip(texture, mip);
        }

        var stored = package.GetBytes(mip.Data.InlineOffset, mip.Data.SizeOnDisk);
        if (mip.Data.Compression == UE3CompressionMethod.None)
        {
            return stored.ToArray();
        }

        using var stream = new MemoryStream(stored.ToArray(), writable: false);
        return UE3Compression.ReadChunk(stream, 0, mip.Data.Compression);
    }

    private static byte[]? DecodePixels(string format, byte[] data, int width, int height)
    {
        // Block-compressed mips are stored at least one 4x4 block across.
        var blockWidth = Math.Max(4, (width + 3) & ~3);
        var blockHeight = Math.Max(4, (height + 3) & ~3);
        byte[]? padded = format switch
        {
            "PF_DXT1" => DecodeBlocks(data, blockWidth, blockHeight, 8, BCDecoder.BC1),
            "PF_DXT3" => DecodeBlocks(data, blockWidth, blockHeight, 16, BCDecoder.BC2),
            "PF_DXT5" => DecodeBlocks(data, blockWidth, blockHeight, 16, BCDecoder.BC3),
            "PF_BC5" or "PF_ATI2" => DecodeBlocks(data, blockWidth, blockHeight, 16, BCDecoder.BC5),
            _ => null,
        };

        if (padded is not null)
        {
            return Crop(padded, blockWidth, width, height);
        }

        return format switch
        {
            "PF_A8R8G8B8" when data.Length >= width * height * 4 => FromBgra(data, width, height),
            "PF_G8" when data.Length >= width * height => FromGray(data, width, height),
            _ => null,
        };
    }

    private delegate void BlockDecoder(ReadOnlySpan<byte> input, int sizeX, int sizeY, Span<byte> output);

    private static byte[]? DecodeBlocks(byte[] data, int width, int height, int blockBytes, BlockDecoder decoder)
    {
        if (data.Length < width / 4 * (height / 4) * blockBytes)
        {
            return null;
        }

        var output = new byte[width * height * 4];
        decoder(data, width, height, output);
        return output;
    }

    private static byte[] Crop(byte[] rgba, int stride, int width, int height)
    {
        if (stride == width && rgba.Length == width * height * 4)
        {
            return rgba;
        }

        var output = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            rgba.AsSpan(row * stride * 4, width * 4).CopyTo(output.AsSpan(row * width * 4));
        }

        return output;
    }

    private static byte[] FromBgra(byte[] data, int width, int height)
    {
        var output = new byte[width * height * 4];
        for (var i = 0; i < output.Length; i += 4)
        {
            output[i] = data[i + 2];
            output[i + 1] = data[i + 1];
            output[i + 2] = data[i];
            output[i + 3] = data[i + 3];
        }

        return output;
    }

    private static byte[] FromGray(byte[] data, int width, int height)
    {
        var output = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            output[i * 4] = output[i * 4 + 1] = output[i * 4 + 2] = data[i];
            output[i * 4 + 3] = 255;
        }

        return output;
    }
}
