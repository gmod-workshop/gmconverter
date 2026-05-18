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
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    pixel.G = (byte)(byte.MaxValue - pixel.G);
                    row[x] = pixel;
                }
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
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    row[x] = new Rgba32(byte.MaxValue, pixel.B, pixel.G, byte.MaxValue);
                }
            }
        });
        return new Texture(textureName ?? $"{Name}_metallic_roughness", output);
    }

    public Texture ToSpecularFactorMask(string? textureName = null)
    {
        // KHR_materials_specular reads the strength multiplier from the texture's ALPHA channel
        // (per the spec: `specular = specularFactor * sample(specularTexture).a`). Fortnite's
        // SpecularMasks store the specular strength in R, so move R -> A and leave RGB at 255 so
        // any consumer that fetches RGB still sees a neutral white. Writing R = G = B = R-value
        // and A = 255 (the old behavior) silently maxed every pixel's specular factor to 1.0,
        // which is why every Fortnite material rendered uniformly glossy.
        var output = _image.Clone(_ => { });
        output.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32(byte.MaxValue, byte.MaxValue, byte.MaxValue, row[x].R);
                }
            }
        });
        return new Texture(textureName ?? $"{Name}_specular_factor", output);
    }

    public Texture ToSourcePhongExponent(string? textureName = null)
    {
        var output = _image.Clone(_ => { });
        output.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var value = (byte)(byte.MaxValue - row[x].G);
                    row[x] = new Rgba32(value, value, value, byte.MaxValue);
                }
            }
        });
        return new Texture(textureName ?? $"{Name}_phong_exponent", output);
    }

    public byte[] ToPngBytes()
    {
        using var ms = new MemoryStream();
        _image.Save(ms, new PngEncoder());
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
