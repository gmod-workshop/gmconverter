using AssetRipper.TextureDecoder.Bc;
using CUE4Parse_Conversion.Textures.BC;
using GMConverter.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GMConverter.Formats.Frostbite;

// Decodes Frostbite TextureAsset chunk bytes to RGBA8 surfaces. The chunk holds the mip pyramid
// in ascending mip order; mip 0 starts at byte 0 with length TextureAsset.MipSizes[0]. Decoding
// dispatches on the SDK-dumped RenderFormat enum (see FrostbiteTextureFormat).
//
// For BCn we route through Detex (via CUE4Parse-Conversion's DetexHelper, which bundles the
// Detex native dll as an embedded resource). Detex produces visually crisp output matching
// FrostyEditor's preview; AssetRipper.TextureDecoder's BC7 in particular produced soft /
// channel-scrambled results on Squadrons textures during testing. BC6H stays on AssetRipper —
// Detex handles BC6H but its BPTC_FLOAT path returns half-floats we'd have to tone-map, while
// AssetRipper already does that step.
internal static class TextureChunkDecoder
{
    public static Image<Rgba32> DecodeMip0(FrostbiteTextureAsset texture, ReadOnlySpan<byte> chunk)
    {
        if (texture.MipSizes.Count == 0 || texture.MipSizes[0] == 0)
        {
            throw new GMConverterException(
                $"TextureAsset {texture.Width}x{texture.Height} reports zero bytes for mip 0; chunk has no data to decode.");
        }

        var mip0Size = (int)texture.MipSizes[0];
        if (mip0Size > chunk.Length)
        {
            throw new GMConverterException(
                $"TextureAsset chunk is {chunk.Length} bytes but mip 0 needs {mip0Size}. Chunk is truncated or the wrong chunk was fetched.");
        }

        // FirstMip is streaming metadata (how many mips were streamed out at runtime), NOT a
        // dimension shift. Wavebend's TextureExporter writes the DDS at the full texture
        // dimensions with MipCount mips and ignores FirstMip for sizing — empirically verified
        // against `T_Fla_Emp_Dash_CarbonizedMynock_01_CS` where FirstMip=5 but mip 0 in the
        // chunk is still the full 1024×1024 image.
        var format = FrostbiteTextureFormatResolver.Resolve(texture.PixelFormatOrdinal);

        // Skip non-2D textures (cubemaps / arrays / volumes). Their chunks contain N slices
        // interleaved per mip; passing them to Detex as if they were flat 2D causes a native
        // access violation because Detex reads past the input buffer. Cubemap support is
        // future work; for now the texture binding silently fails (better than crashing).
        if (texture.Type != 0 || texture.Depth > 1 || texture.SliceCount > 1)
        {
            throw new GMConverterException(
                $"TextureAsset {texture.Width}×{texture.Height} type={texture.Type} depth={texture.Depth} slices={texture.SliceCount} is not a flat 2D texture; preview decoder skips.");
        }

        // FirstMip is streaming metadata: when >0 it indicates the high-detail mips have been
        // streamed out and only the lower mips are resident in this chunk. For most assets the
        // chunk still has the full pyramid (FirstMip is just hint metadata, e.g., the mynock CS
        // texture has FirstMip=5 but mip 0 is the full 1024×1024 image). For some assets — in
        // particular the larger vehicle textures — only the resident mips ship in the chunk.
        // Derive the actual resident-mip dimensions from MipSizes[0] + format to handle both.
        var (width, height) = DeriveResidentDimensions(texture, format, mip0Size);

        // Last-resort safety net: if dimensions still don't match mip0Size for the format,
        // refuse to call Detex (which would AV on buffer underrun). This is the path we hit
        // when the resident-dimension derivation logs its fallback.
        var expectedMip0 = ExpectedMip0Bytes(width, height, format);
        if (expectedMip0 > 0 && expectedMip0 != mip0Size)
        {
            throw new GMConverterException(
                $"TextureAsset {texture.Width}×{texture.Height} ord={texture.PixelFormatOrdinal} ({format}) mip0Size={mip0Size} but derived dimensions {width}×{height} expect {expectedMip0} bytes; refusing to decode (would crash Detex).");
        }

        var mip0Bytes = chunk[..mip0Size].ToArray();

        return format switch
        {
            FrostbiteTextureFormat.Bc1Unorm or FrostbiteTextureFormat.Bc1Srgb
                => DecodeDetex(mip0Bytes, width, height, DetexTextureFormat.DETEX_TEXTURE_FORMAT_BC1),

            FrostbiteTextureFormat.Bc1AUnorm or FrostbiteTextureFormat.Bc1ASrgb
                => DecodeDetex(mip0Bytes, width, height, DetexTextureFormat.DETEX_TEXTURE_FORMAT_BC1A),

            FrostbiteTextureFormat.Bc2Unorm or FrostbiteTextureFormat.Bc2Srgb
                => DecodeDetex(mip0Bytes, width, height, DetexTextureFormat.DETEX_TEXTURE_FORMAT_BC2),

            FrostbiteTextureFormat.Bc3Unorm or FrostbiteTextureFormat.Bc3Srgb
                => DecodeDetex(mip0Bytes, width, height, DetexTextureFormat.DETEX_TEXTURE_FORMAT_BC3),

            // BC4 + BC5 go through AssetRipper, not Detex. Detex's RGTC1/RGTC2 paths to RGBA8
            // are unreliable for these single/two-channel formats and trigger native AVs on
            // some Squadrons assets (e.g. `_e` emissive textures encoded as BC4). CUE4Parse
            // itself avoids Detex for BC4/BC5 for the same reason.
            FrostbiteTextureFormat.Bc4Unorm
                => DecodeBcnAssetRipper(mip0Bytes, width, height, Bc4.Decompress),

            FrostbiteTextureFormat.Bc5Unorm
                => DecodeBcnAssetRipper(mip0Bytes, width, height, Bc5.Decompress),

            FrostbiteTextureFormat.Bc6UFloat or FrostbiteTextureFormat.Bc6SFloat
                => DecodeBc6h(mip0Bytes, width, height, format == FrostbiteTextureFormat.Bc6SFloat),

            FrostbiteTextureFormat.Bc7Unorm or FrostbiteTextureFormat.Bc7Srgb
                => DecodeDetex(mip0Bytes, width, height, DetexTextureFormat.DETEX_TEXTURE_FORMAT_BPTC),

            FrostbiteTextureFormat.R8Unorm
                => DecodeR8(mip0Bytes, width, height),

            FrostbiteTextureFormat.R8g8b8a8Unorm or FrostbiteTextureFormat.R8g8b8a8Srgb
                => DecodeRgba8(mip0Bytes, width, height),

            _ => throw new GMConverterException(
                $"TextureAsset {width}x{height} pixelFormat={texture.PixelFormatOrdinal} ({format}) has no decoder mapping.")
        };
    }

    private static Image<Rgba32> DecodeDetex(byte[] mipBytes, int width, int height, DetexTextureFormat format)
    {
        var rgba = DetexHelper.DecodeDetexLinear(mipBytes, width, height, false, format, DetexPixelFormat.DETEX_PIXEL_FORMAT_RGBA8);
        return Image.LoadPixelData<Rgba32>(rgba, width, height);
    }

    private delegate int BcDecompress(ReadOnlySpan<byte> input, int width, int height, out byte[] output);

    private static Image<Rgba32> DecodeBcnAssetRipper(byte[] mipBytes, int width, int height, BcDecompress decompress)
    {
        decompress(mipBytes, width, height, out var rgba);
        return Image.LoadPixelData<Rgba32>(rgba, width, height);
    }

    private static Image<Rgba32> DecodeBc6h(ReadOnlySpan<byte> mipBytes, int width, int height, bool signed)
    {
        // AssetRipper's Bc6h.Decompress tone-maps the HDR data to RGBA8 internally. Detex returns
        // half-floats and would need an extra tone-map pass, so we keep BC6H on AssetRipper.
        Bc6h.Decompress(mipBytes, width, height, signed, out var rgba);
        return Image.LoadPixelData<Rgba32>(rgba, width, height);
    }

    private static Image<Rgba32> DecodeR8(ReadOnlySpan<byte> mipBytes, int width, int height)
    {
        // Single-channel R8 expands to RGBA by replicating the value into R/G/B and setting A=255.
        // Frostbite uses this for grayscale masks; treating it as a luminance image is the
        // sensible visual default.
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            var v = mipBytes[i];
            rgba[i * 4 + 0] = v;
            rgba[i * 4 + 1] = v;
            rgba[i * 4 + 2] = v;
            rgba[i * 4 + 3] = 255;
        }
        return Image.LoadPixelData<Rgba32>(rgba, width, height);
    }

    private static Image<Rgba32> DecodeRgba8(ReadOnlySpan<byte> mipBytes, int width, int height)
    {
        // R8G8B8A8 is already in the layout ImageSharp expects, so this is a direct copy.
        return Image.LoadPixelData<Rgba32>(mipBytes, width, height);
    }

    // Walk down the mip pyramid (halving dimensions each step) until mip0Size matches the
    // expected byte count for the current dimensions. This handles streaming textures where
    // FirstMip > 0 and the chunk truly starts at a smaller mip than the texture's declared
    // Width × Height. Passing oversized dimensions to Detex causes a native access violation
    // because Detex doesn't bounds-check against the input buffer length.
    private static (int Width, int Height) DeriveResidentDimensions(
        FrostbiteTextureAsset texture, FrostbiteTextureFormat format, int mip0Size)
    {
        var w = texture.Width;
        var h = texture.Height;
        for (var step = 0; step < 16; step++)
        {
            var expected = ExpectedMip0Bytes(w, h, format);
            if (expected == mip0Size)
            {
                return (w, h);
            }
            if (w <= 1 && h <= 1)
            {
                break;
            }
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        // Couldn't reconcile — fall back to declared dimensions and let the caller's
        // post-derivation safety check reject. Logged so we know to investigate.
        PerfTimer.Log("frostbite.texture",
            $"Could not derive resident dimensions for {texture.Width}×{texture.Height} ord={texture.PixelFormatOrdinal} mip0Size={mip0Size}; using declared.");
        return (texture.Width, texture.Height);
    }

    private static int ExpectedMip0Bytes(int width, int height, FrostbiteTextureFormat format)
    {
        var bytesPerBlock = format switch
        {
            FrostbiteTextureFormat.Bc1Unorm or FrostbiteTextureFormat.Bc1Srgb or
            FrostbiteTextureFormat.Bc1AUnorm or FrostbiteTextureFormat.Bc1ASrgb or
            FrostbiteTextureFormat.Bc4Unorm => 8,
            FrostbiteTextureFormat.Bc2Unorm or FrostbiteTextureFormat.Bc2Srgb or
            FrostbiteTextureFormat.Bc3Unorm or FrostbiteTextureFormat.Bc3Srgb or
            FrostbiteTextureFormat.Bc5Unorm or
            FrostbiteTextureFormat.Bc6UFloat or FrostbiteTextureFormat.Bc6SFloat or
            FrostbiteTextureFormat.Bc7Unorm or FrostbiteTextureFormat.Bc7Srgb => 16,
            _ => 0
        };

        if (bytesPerBlock > 0)
        {
            return Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * bytesPerBlock;
        }

        return format switch
        {
            FrostbiteTextureFormat.R8Unorm => width * height,
            FrostbiteTextureFormat.R8g8b8a8Unorm or FrostbiteTextureFormat.R8g8b8a8Srgb => width * height * 4,
            _ => 0
        };
    }
}
