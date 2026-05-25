using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using GMConverter.UnrealEngine.Common;
using SkiaSharp;

namespace GMConverter.UnrealEngine.Formats.Unreal;

// Single source of truth for "turn a UTexture2D into a DecodedImage" inside the codebase. The
// path lives in Formats.Unreal because the input type (UTexture2D) and the underlying decoder
// (CUE4Parse + SkiaSharp) are both UE-specific, but the output (DecodedImage) is the generic
// cache-friendly shape any importer/exporter can consume.
//
// Why a dedicated helper: UE4Explorer.cs is already 2900+ lines and this decode path is reused
// by MultiLayerBaker, the per-part material extractor, and the TextureData override writer. A
// single function with a single responsibility is easier to maintain than three copies of the
// CUE4Parse → SkBitmap → managed bytes ceremony.
internal static class Cue4ParseImageDecoder
{
    public static DecodedImage Decode(UTexture2D texture)
    {
        try
        {
            if (texture.Decode(ETexturePlatform.DesktopMobile) is not { } bitmap)
            {
                return DecodedImage.Empty;
            }

            // CUE4Parse's CTexture.Data is in whatever pixel format the source mip used; ToSkBitmap
            // normalizes via SkiaSharp into either Rgba8888 or Bgra8888 depending on the input.
            // Copying to a canonical Rgba8888 bitmap gives us a single managed byte[] we can hand
            // to any consumer (ImageSharp LoadPixelData, our own raster, etc.) without per-format
            // branching at every call site.
            using var skBitmap = bitmap.ToSkBitmap();
            if (skBitmap.Width <= 0 || skBitmap.Height <= 0)
            {
                return DecodedImage.Empty;
            }

            using var normalized = skBitmap.Copy(SKColorType.Rgba8888);
            if (normalized is null)
            {
                return DecodedImage.Empty;
            }

            var bytes = normalized.Bytes;
            if (bytes.Length == 0)
            {
                return DecodedImage.Empty;
            }

            return new DecodedImage(bytes, normalized.Width, normalized.Height, DecodedImagePixelFormat.Rgba8888);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Decode failures are intentionally non-fatal — fall back to Empty so the caller
            // can use a placeholder or skip the texture. Process-fatal exceptions (OOM)
            // propagate so a real memory crisis isn't disguised as a benign decode miss.
            return DecodedImage.Empty;
        }
    }
}
