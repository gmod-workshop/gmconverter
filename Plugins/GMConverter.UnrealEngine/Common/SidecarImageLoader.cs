using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GMConverter.UnrealEngine.Common;

// UModel writes 32-bit TGAs whose image descriptor declares zero alpha bits even when the fourth
// channel carries real opacity (e.g. Republic Commando's bacta fluid). ImageSharp trusts the
// descriptor and decodes those pixels as fully opaque, which silently turns translucent materials
// solid. When the caller wants alpha, relabel the descriptor as 8 alpha bits before decoding so the
// stored channel survives. A relabelled image whose alpha comes back entirely zero is treated as
// padding rather than opacity and decoded as the header described.
internal static class SidecarImageLoader
{
    private const int _headerLength = 18;
    private const int _imageTypeOffset = 2;
    private const int _pixelDepthOffset = 16;
    private const int _descriptorOffset = 17;
    private const byte _alphaBitsMask = 0x0F;

    public static Image<Rgba32> Load(string path, bool preserveAlpha)
    {
        if (!preserveAlpha || !Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase))
        {
            return Image.Load<Rgba32>(path);
        }

        var bytes = File.ReadAllBytes(path);
        if (!HasUnlabeledAlphaChannel(bytes))
        {
            return Image.Load<Rgba32>(bytes);
        }

        bytes[_descriptorOffset] |= 8;
        var image = Image.Load<Rgba32>(bytes);
        if (!IsFullyTransparent(image))
        {
            return image;
        }

        image.Dispose();
        bytes[_descriptorOffset] &= unchecked((byte)~_alphaBitsMask);
        return Image.Load<Rgba32>(bytes);
    }

    private static bool HasUnlabeledAlphaChannel(byte[] bytes)
    {
        // Image types 2 and 10 are uncompressed and RLE true-color.
        return bytes.Length > _headerLength &&
            bytes[_imageTypeOffset] is 2 or 10 &&
            bytes[_pixelDepthOffset] == 32 &&
            (bytes[_descriptorOffset] & _alphaBitsMask) == 0;
    }

    private static bool IsFullyTransparent(Image<Rgba32> image)
    {
        var transparent = true;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height && transparent; y++)
            {
                foreach (var pixel in accessor.GetRowSpan(y))
                {
                    if (pixel.A != 0)
                    {
                        transparent = false;
                        break;
                    }
                }
            }
        });
        return transparent;
    }
}
