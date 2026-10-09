namespace GMConverter.UnrealEngine.Common;

// Decoded raw-pixel image, format-agnostic enough to be cached in ExportSessionCache and consumed
// by any importer/exporter regardless of which image library it ultimately uses (ImageSharp,
// SkiaSharp, raw byte spans). The cache stores instances of this type when a consumer wants to
// avoid going through an encode-then-decode roundtrip — e.g. the UE multi-layer baker reads raw
// RGBA pixel data directly into an ImageSharp Image, skipping PNG entirely.
//
// PNG encoding is lazy and memoized per instance so consumers that DO need PNG bytes (typically
// because they're writing per-part PNG files to disk) still only pay the encode cost once per
// unique source texture for the lifetime of the cache entry. The encoder function is injected so
// this type doesn't depend on any particular image library — callers in GMConverter can use
// ImageSharp, callers in a future native-deps consumer could use SkiaSharp or libpng directly.
internal sealed class DecodedImage
{
    public byte[] Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    public DecodedImagePixelFormat Format { get; }

    private byte[]? _encodedPng;

    public DecodedImage(byte[] pixels, int width, int height, DecodedImagePixelFormat format)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        Format = format;
    }

    public bool IsValid => Pixels.Length > 0 && Width > 0 && Height > 0;

    public static DecodedImage Empty { get; } = new([], 0, 0, DecodedImagePixelFormat.Rgba8888);

    // Returns memoized PNG bytes for this image, computing them on first call via the supplied
    // encoder. Subsequent calls (from any thread) return the same byte[] — the
    // Interlocked.CompareExchange ensures only the winning thread's bytes get cached even if two
    // threads racing on a cold instance both invoke the encoder.
    public byte[] GetOrEncodePng(Func<DecodedImage, byte[]> encoder)
    {
        var existing = _encodedPng;
        if (existing is not null)
        {
            return existing;
        }
        var encoded = encoder(this);
        return Interlocked.CompareExchange(ref _encodedPng, encoded, null) ?? encoded;
    }
}

internal enum DecodedImagePixelFormat
{
    Rgba8888,
    Bgra8888
}
