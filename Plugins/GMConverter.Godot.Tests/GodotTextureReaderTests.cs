using System.Text;
using GMConverter.Core.Textures;
using GMConverter.Godot.Textures;

namespace GMConverter.Godot.Tests;

public sealed class GodotTextureReaderTests
{
    [Fact]
    public void ReadsRawImage()
    {
        byte[] pixels = [255, 0, 0, 255, 0, 255, 0, 128];
        var texture = Build(width: 2, height: 1, format: GodotImageFormat.Rgba8, pixels);

        var image = GodotTextureReader.Read(texture, "pair", new DefaultTextureFactory());

        Assert.NotNull(image);
        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(pixels, image.Rgba);
        Assert.False(image.IsNormalMap);
    }

    [Fact]
    public void ExpandsLuminanceAlpha()
    {
        var texture = Build(width: 1, height: 1, format: GodotImageFormat.La8, [200, 50]);

        var image = GodotTextureReader.Read(texture, "grey", new DefaultTextureFactory());

        Assert.Equal([200, 200, 200, 50], image!.Rgba);
    }

    [Fact]
    public void RejectsOtherFiles()
    {
        Assert.Null(GodotTextureReader.Read(new byte[64], "none", new DefaultTextureFactory()));
    }

    // A GST2 file: the header (magic, version, size, flags, mipmap limit, three reserved values),
    // then the image (data format 0 = raw, size, mipmap count, format) and its pixels.
    private static byte[] Build(int width, int height, GodotImageFormat format, byte[] pixels)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write("GST2"u8);
        writer.Write(1);
        writer.Write(width);
        writer.Write(height);
        writer.Write(0);
        writer.Write(-1);
        writer.Write(new byte[12]);
        writer.Write(0);
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write(0);
        writer.Write((int)format);
        writer.Write(pixels);
        writer.Flush();
        return stream.ToArray();
    }
}
