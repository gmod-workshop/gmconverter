namespace GMConverter.SDK.Textures;

/// <summary>
/// Abstract texture contract for the GMConverter SDK. The concrete implementation (and any
/// dependency on a specific image library) lives in the host. Plugins consume textures through
/// this surface and construct them via the importer-side factory provided by the host.
/// </summary>
public abstract class Texture : IDisposable
{
    public abstract string Name { get; }

    public abstract string? Path { get; }

    public abstract bool HasAlpha { get; }

    public abstract int Width { get; }

    public abstract int Height { get; }

    public abstract string DebugDimensions { get; }

    /// <summary>
    /// Returns a copy of this texture scaled so its longest edge is at most <paramref name="maxDimension"/>,
    /// preserving aspect ratio. Returns the same instance when already within the cap or when
    /// <paramref name="maxDimension"/> is non-positive.
    /// </summary>
    public abstract Texture Resized(int maxDimension);

    /// <summary>
    /// Stable hash over dimensions and raw pixel data. Used by exporters to deduplicate identical
    /// textures across materials.
    /// </summary>
    public abstract ulong ContentHash();

    public abstract byte[] ToPngBytes();

    public abstract void WritePng(string path);

    public abstract void WritePng(string path, Texture alphaMask);

    public abstract void WriteTga(string path);

    public abstract void WriteTga(string path, Texture alphaMask);

    public abstract void Dispose();
}
