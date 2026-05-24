namespace GMConverter.SDK.Textures;

/// <summary>
/// Constructs <see cref="Texture"/> instances on behalf of plugins. The host injects an
/// implementation via <see cref="Plugins.IPluginContext.TextureFactory"/>, keeping the
/// concrete image library a host-side choice. Plugin code never references the underlying
/// image library directly.
/// </summary>
public interface ITextureFactory
{
    /// <summary>
    /// Load a texture from a file path. Decoding is delegated to the host's image library.
    /// </summary>
    /// <param name="path">Filesystem path to the source image.</param>
    /// <param name="hasAlpha">Whether the source image's alpha channel carries meaningful data.</param>
    /// <param name="name">Optional name override; defaults to the file name without extension.</param>
    Texture FromFile(string path, bool hasAlpha = false, string? name = null);

    /// <summary>
    /// Load a texture from a stream. The stream is read synchronously; it remains owned by the caller.
    /// </summary>
    Texture FromStream(string name, Stream stream, bool hasAlpha = false, string? path = null);

    /// <summary>
    /// Construct a texture from raw RGBA8888 pixel data in row-major order. The byte span is
    /// copied into the texture, so the caller may free the source after this returns.
    /// </summary>
    Texture FromRgba(
        string name,
        int width,
        int height,
        ReadOnlySpan<byte> rgbaPixels,
        bool hasAlpha = false,
        string? path = null);
}
