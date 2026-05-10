using ImageMagick;

namespace GMConverter.Geometry;

internal sealed class Texture(string name, MagickImage image, bool hasAlpha = false, string? path = null)
{
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

    public void WriteTga(string path)
    {
        Write(path, MagickFormat.Tga);
    }

    private void Write(string path, MagickFormat format)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");

        using var output = (MagickImage)image.Clone();
        output.Format = format;
        output.Write(path);
    }
}
