using GMConverter.Geometry;
using GMConverter.SDK.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GMConverter.Plugins;

/// <summary>
/// Default <see cref="ITextureFactory"/> implementation. Produces <see cref="ImageSharpTexture"/>
/// instances backed by SixLabors.ImageSharp. The host injects this into <see cref="DefaultPluginContext"/>
/// so plugin code can construct textures without a direct ImageSharp reference. Public so plugin
/// test projects can build textures the same way the host does.
/// </summary>
public sealed class DefaultTextureFactory : ITextureFactory
{
    public Texture FromFile(string path, bool hasAlpha = false, string? name = null)
    {
        var image = Image.Load<Rgba32>(path);
        return new ImageSharpTexture(name ?? Path.GetFileNameWithoutExtension(path), image, hasAlpha, path);
    }

    public Texture FromStream(string name, Stream stream, bool hasAlpha = false, string? path = null)
    {
        var image = Image.Load<Rgba32>(stream);
        return new ImageSharpTexture(name, image, hasAlpha, path);
    }

    public Texture FromRgba(
        string name,
        int width,
        int height,
        ReadOnlySpan<byte> rgbaPixels,
        bool hasAlpha = false,
        string? path = null)
    {
        var image = Image.LoadPixelData<Rgba32>(rgbaPixels, width, height);
        return new ImageSharpTexture(name, image, hasAlpha, path);
    }
}
