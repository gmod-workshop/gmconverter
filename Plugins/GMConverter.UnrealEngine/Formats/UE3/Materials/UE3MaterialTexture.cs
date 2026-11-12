using System.Numerics;
using GMConverter.UnrealEngine.Formats.UE3.Textures;
using GMConverter.UnrealEngine.Materials;

namespace GMConverter.UnrealEngine.Formats.UE3.Materials;

/// <summary>
/// A decoded texture sampled the way the material samples it: bilinear, with the texture's own
/// addressing, and with sRGB colour textures converted to linear.
/// </summary>
internal sealed class UE3MaterialTexture
{
    private readonly Vector4[] _texels;

    public UE3MaterialTexture(DecodedImage image, UE3Texture2D texture)
    {
        Width = image.Width;
        Height = image.Height;
        AddressX = texture.AddressX;
        AddressY = texture.AddressY;
        _texels = new Vector4[Width * Height];
        Span<float> toLinear = stackalloc float[256];
        for (var i = 0; i < 256; i++)
        {
            toLinear[i] = texture.Srgb ? SrgbToLinear(i / 255f) : i / 255f;
        }

        var pixels = image.Pixels;
        for (var i = 0; i < _texels.Length; i++)
        {
            _texels[i] = new Vector4(toLinear[pixels[i * 4]], toLinear[pixels[i * 4 + 1]], toLinear[pixels[i * 4 + 2]], pixels[i * 4 + 3] / 255f);
        }
    }

    public int Width { get; }

    public int Height { get; }

    public UE3TextureAddress AddressX { get; }

    public UE3TextureAddress AddressY { get; }

    public Vector4 Sample(Vector2 uv)
    {
        var x = uv.X * Width - 0.5f;
        var y = uv.Y * Height - 0.5f;
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        var top = Vector4.Lerp(Texel(x0, y0), Texel(x0 + 1, y0), fx);
        var bottom = Vector4.Lerp(Texel(x0, y0 + 1), Texel(x0 + 1, y0 + 1), fx);
        return Vector4.Lerp(top, bottom, fy);
    }

    public static float SrgbToLinear(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    public static float LinearToSrgb(float value) => value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;

    private Vector4 Texel(int x, int y) => _texels[Address(y, Height, AddressY) * Width + Address(x, Width, AddressX)];

    private static int Address(int coordinate, int size, UE3TextureAddress address)
    {
        switch (address)
        {
            case UE3TextureAddress.Clamp:
                return Math.Clamp(coordinate, 0, size - 1);
            case UE3TextureAddress.Mirror:
                var period = size * 2;
                var wrapped = ((coordinate % period) + period) % period;
                return wrapped < size ? wrapped : period - 1 - wrapped;
            default:
                return ((coordinate % size) + size) % size;
        }
    }
}
