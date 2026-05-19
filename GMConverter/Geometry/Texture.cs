using System.Numerics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GMConverter.Geometry;

internal sealed class Texture : IDisposable
{
    private readonly Image<Rgba32> _image;

    public Texture(string name, Image<Rgba32> image, bool hasAlpha = false, string? path = null)
    {
        _image = image;
        Name = name;
        Path = path;
        HasAlpha = hasAlpha;
    }

    public string Name { get; }

    public string? Path { get; }

    public bool HasAlpha { get; }

    public int Width => _image.Width;

    public int Height => _image.Height;

    public string DebugDimensions => $"{_image.Width}x{_image.Height} pixel={typeof(Rgba32).Name}";

    public Texture WithOpenGlNormalMap(string? textureName = null)
    {
        var output = _image.Clone(_ => { });
        output.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var bytes = MemoryMarshal.AsBytes(accessor.GetRowSpan(y));
                InvertGreenChannel(bytes);
            }
        });
        return new Texture(textureName ?? $"{Name}_gl", output);
    }

    public Texture ToGltfMetallicRoughness(string? textureName = null)
    {
        // Fortnite SpecularMasks pack as R=Specular(unused), G=Metallic, B=Roughness, A=custom.
        // glTF KHR metallicRoughness expects G=Roughness, B=Metallic — so we swap. Verified against
        // FModel's default.frag where specular_masks.g feeds schlickFresnel (metallic) and
        // specular_masks.b feeds the roughness mix.
        var output = _image.Clone(_ => { });
        output.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var bytes = MemoryMarshal.AsBytes(accessor.GetRowSpan(y));
                // R=255, G=B_in, B=G_in, A=255 — direct byte writes avoid the Rgba32 struct
                // ceremony from the previous property-accessor loop.
                for (var i = 0; i + 4 <= bytes.Length; i += 4)
                {
                    var gIn = bytes[i + 1];
                    var bIn = bytes[i + 2];
                    bytes[i] = 255;
                    bytes[i + 1] = bIn;
                    bytes[i + 2] = gIn;
                    bytes[i + 3] = 255;
                }
            }
        });
        return new Texture(textureName ?? $"{Name}_metallic_roughness", output);
    }

    public Texture ToSpecularFactorMask(string? textureName = null)
    {
        // Write Fortnite's SpecularMasks.R into both RGB and Alpha so downstream exporters that
        // sample either channel get the right value:
        //   - glTF KHR_materials_specular reads `specularTexture.A` per the spec.
        //   - Source MDL's `$phongmask` is composited as the alpha channel of the basetexture,
        //     but our `ApplyAlphaMask` helper composites the mask's RED channel into the diffuse's
        //     alpha (per its docstring) — so the mask needs the value in R, not just A.
        // Putting the value in all four channels covers both cases without per-exporter branching.
        var output = _image.Clone(_ => { });
        output.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var bytes = MemoryMarshal.AsBytes(accessor.GetRowSpan(y));
                for (var i = 0; i + 4 <= bytes.Length; i += 4)
                {
                    var r = bytes[i];
                    bytes[i + 1] = r;
                    bytes[i + 2] = r;
                    bytes[i + 3] = r;
                }
            }
        });
        return new Texture(textureName ?? $"{Name}_specular_factor", output);
    }

    // Vectorized green-channel inversion: builds a {0,0xFF,0,0,…} mask matching Vector<byte>.Count
    // and computes ((0xFF - v) & mask) | (v & ~mask) in chunks of 16/32/64 bytes depending on the
    // hardware SIMD width. Scalar fallback for the tail and for the no-SIMD case. The transform is
    // called once per material that uses a DirectX normal map (every Fortnite glTF export hits this
    // path repeatedly), so collapsing it from a per-pixel struct read/write to a SIMD-friendly
    // byte pass is a meaningful save on top of the parallel BuildMaterials step.
    private static void InvertGreenChannel(Span<byte> rgba)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated && rgba.Length >= Vector<byte>.Count)
        {
            var width = Vector<byte>.Count;
            Span<byte> maskBuffer = stackalloc byte[width];
            for (var j = 0; j < width; j++)
            {
                maskBuffer[j] = (j & 3) == 1 ? (byte)0xFF : (byte)0;
            }
            var mask = new Vector<byte>(maskBuffer);
            var notMask = Vector.OnesComplement(mask);
            var allOnes = Vector<byte>.AllBitsSet;

            for (; i + width <= rgba.Length; i += width)
            {
                var v = new Vector<byte>(rgba.Slice(i, width));
                var inverted = allOnes - v;
                var result = (inverted & mask) | (v & notMask);
                result.CopyTo(rgba.Slice(i, width));
            }
        }

        for (; i + 4 <= rgba.Length; i += 4)
        {
            bytes_invert_g(rgba, i);
        }

        static void bytes_invert_g(Span<byte> rgba, int i)
        {
            rgba[i + 1] = (byte)(255 - rgba[i + 1]);
        }
    }

    public Texture ToSourcePhongExponent(string? textureName = null)
    {
        // Fortnite SpecularMasks packing: R=Specular, G=Metallic, B=Roughness.
        // Source `$phongexponenttexture` packing: R=per-pixel exponent scale (multiplied with
        // `$phongexponent`), A=phong mask (only used when `$basemapalphaphongmask` is not set, so
        // the basetexture alpha can be free for `$translucent`).
        // Mapping: invert roughness (smooth surfaces → sharp highlights). Phong mask = SpecularMasks.R
        // so the same per-pixel "how much specular here" carries through whether the consumer uses
        // glTF KHR_materials_specular or Source phong.
        var output = _image.Clone(_ => { });
        output.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var src = row[x];
                    var exponent = (byte)(byte.MaxValue - src.B);
                    row[x] = new Rgba32(exponent, exponent, exponent, src.R);
                }
            }
        });
        return new Texture(textureName ?? $"{Name}_phong_exponent", output);
    }

    // Returns a copy of this texture with its alpha channel replaced by the mask's red channel.
    // Used for Source's `$normalmapalphaenvmapmask` workflow, where the envmap mask must live in
    // the normal map's alpha rather than a separate texture (Source rejects $envmapmask alongside
    // $bumpmap due to pixel-shader register limits in VertexLitGeneric).
    public Texture WithMaskInAlpha(Texture mask, string? textureName = null)
    {
        var output = _image.Clone(_ => { });
        using var maskImage = mask._image.Clone(_ => { });
        if (maskImage.Width != output.Width || maskImage.Height != output.Height)
        {
            maskImage.Mutate(ctx => ctx.Resize(output.Width, output.Height));
        }
        output.ProcessPixelRows(maskImage, (outAccessor, maskAccessor) =>
        {
            for (var y = 0; y < outAccessor.Height; y++)
            {
                var outRow = outAccessor.GetRowSpan(y);
                var maskRow = maskAccessor.GetRowSpan(y);
                for (var x = 0; x < outRow.Length; x++)
                {
                    var pixel = outRow[x];
                    outRow[x] = new Rgba32(pixel.R, pixel.G, pixel.B, maskRow[x].R);
                }
            }
        });
        return new Texture(textureName ?? $"{Name}_with_mask", output, hasAlpha: true);
    }

    // Default DEFLATE level 6 was costing ~13s across a 22-part Fortnite scene's glTF export — the
    // bottleneck once the resolve and scan caches removed everything ahead of it. Level 1 produces
    // PNGs ~10-15% larger than level 6 but encodes 3-4x faster, which is the right trade for a
    // preview/export workflow where iteration speed matters more than wire size.
    private static readonly PngEncoder _fastPngEncoder = new() { CompressionLevel = PngCompressionLevel.Level1 };

    public byte[] ToPngBytes()
    {
        using var ms = new MemoryStream();
        _image.Save(ms, _fastPngEncoder);
        return ms.ToArray();
    }

    public void WritePng(string path)
    {
        Write(path, new PngEncoder(), alphaMask: null);
    }

    public void WritePng(string path, Texture alphaMask)
    {
        Write(path, new PngEncoder(), alphaMask);
    }

    public void WriteTga(string path)
    {
        Write(path, new TgaEncoder(), alphaMask: null);
    }

    public void WriteTga(string path, Texture alphaMask)
    {
        Write(path, new TgaEncoder(), alphaMask);
    }

    public void Dispose()
    {
        _image.Dispose();
    }

    private void Write(string path, ImageEncoder encoder, Texture? alphaMask)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");

        using var output = _image.Clone(_ => { });
        if (alphaMask is not null)
        {
            ApplyAlphaMask(output, alphaMask);
        }

        output.Save(path, encoder);
    }

    private static void ApplyAlphaMask(Image<Rgba32> output, Texture alphaMask)
    {
        using var mask = alphaMask._image.Clone(_ => { });
        if (mask.Width != output.Width || mask.Height != output.Height)
        {
            mask.Mutate(ctx => ctx.Resize(output.Width, output.Height));
        }

        // Composite the mask's red channel onto the output's alpha channel. Mirrors Magick.NET's
        // `image.Composite(mask, CompositeOperator.CopyAlpha)` after converting the mask to gray.
        output.ProcessPixelRows(mask, (outAccessor, maskAccessor) =>
        {
            for (var y = 0; y < outAccessor.Height; y++)
            {
                var outRow = outAccessor.GetRowSpan(y);
                var maskRow = maskAccessor.GetRowSpan(y);
                for (var x = 0; x < outRow.Length; x++)
                {
                    var pixel = outRow[x];
                    pixel.A = maskRow[x].R;
                    outRow[x] = pixel;
                }
            }
        });
    }

    private Texture ToSingleChannelTexture(string textureName, int channelIndex)
    {
        var output = _image.Clone(_ => { });
        output.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    var value = channelIndex switch
                    {
                        0 => pixel.R,
                        1 => pixel.G,
                        2 => pixel.B,
                        _ => pixel.A,
                    };
                    row[x] = new Rgba32(value, value, value, byte.MaxValue);
                }
            }
        });
        return new Texture(textureName, output);
    }
}
