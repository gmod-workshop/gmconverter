using ImageMagick;

namespace GMConverter.Geometry;

internal sealed class Texture(string name, MagickImage image, bool hasAlpha = false, string? path = null)
{
    private readonly MagickImage image = image;

    public string Name { get; } = name;

    public string? Path { get; } = path;

    public bool HasAlpha { get; } = hasAlpha;

    public byte[] ToPngBytes()
    {
        using var output = (MagickImage)image.Clone();
        output.Format = MagickFormat.Png;
        return output.ToByteArray();
    }

    public void WritePng(string path)
    {
        Write(path, MagickFormat.Png);
    }

    public void WritePng(string path, Texture alphaMask)
    {
        Write(path, MagickFormat.Png, alphaMask);
    }

    public void WriteTga(string path)
    {
        Write(path, MagickFormat.Tga);
    }

    public void WriteTga(string path, Texture alphaMask)
    {
        Write(path, MagickFormat.Tga, alphaMask);
    }

    private void Write(string path, MagickFormat format, Texture? alphaMask = null)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");

        using var output = (MagickImage)image.Clone();
        if (alphaMask is not null)
        {
            ApplyAlphaMask(output, alphaMask);
        }

        output.Format = format;
        output.Write(path);
    }

    private static void ApplyAlphaMask(MagickImage output, Texture alphaMask)
    {
        using var mask = (MagickImage)alphaMask.image.Clone();
        if (mask.Width != output.Width || mask.Height != output.Height)
        {
            mask.Resize(output.Width, output.Height);
        }

        mask.ColorSpace = ColorSpace.Gray;
        output.Alpha(AlphaOption.Set);
        output.Composite(mask, CompositeOperator.CopyAlpha);
    }
}
