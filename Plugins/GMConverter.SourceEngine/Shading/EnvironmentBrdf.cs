using GMConverter.SourceEngine.Particles;

namespace GMConverter.SourceEngine.Shading;

/// <summary>
/// The environment BRDF table lit shaders weigh reflections by: for roughness r (across) and N.V
/// (down), the scale A and bias B with which <c>F0 * A + F90 * B</c> is the GGX model of
/// <see cref="SDK.Materials.MaterialShading.UnrealGgx"/> integrated over the hemisphere under uniform
/// light (Karis' split sum). CrossFire HD builds its <c>PreIntegratedBRDFTexture</c> in engine code
/// rather than shipping it, so this integrates the model its shaders use: the GGX distribution of
/// a = r², Smith's joint visibility approximation of a, and Schlick's Fresnel, importance-sampled.
/// Written as an 8-bit VTF, A in red and B in green: Source rescales float textures in its integer
/// HDR mode.
/// </summary>
internal static class EnvironmentBrdf
{
    public const int Size = 128;
    private const int _samples = 1024;

    /// <summary>The table's (A, B) at the texel centre (column, row).</summary>
    public static (float A, float B) Integrate(int column, int row)
    {
        var roughness = (column + 0.5) / Size;
        var nv = (row + 0.5) / Size;
        var a = roughness * roughness;
        var v = (X: Math.Sqrt(1 - (nv * nv)), Z: nv);
        double scale = 0, bias = 0;
        for (var sample = 0; sample < _samples; sample++)
        {
            // Hammersley point, then a half vector distributed as GGX(a).
            var u = (sample + 0.5) / _samples;
            var w = RadicalInverse((uint)sample);
            var phi = 2 * Math.PI * u;
            var cosTheta = Math.Sqrt((1 - w) / (1 + ((a * a) - 1) * w));
            var sinTheta = Math.Sqrt(1 - (cosTheta * cosTheta));
            var h = (X: sinTheta * Math.Cos(phi), Y: sinTheta * Math.Sin(phi), Z: cosTheta);
            var vh = (v.X * h.X) + (v.Z * h.Z);
            var nl = (2 * vh * h.Z) - v.Z;
            if (nl <= 0)
            {
                continue;
            }

            var nh = h.Z;
            var visibility = 0.5 / ((nl * ((nv * (1 - a)) + a)) + (nv * ((nl * (1 - a)) + a)));
            var weighted = visibility * 4 * nl * vh / nh;
            var fresnel = Math.Pow(1 - vh, 5);
            scale += (1 - fresnel) * weighted;
            bias += fresnel * weighted;
        }

        return ((float)(scale / _samples), (float)(bias / _samples));
    }

    /// <summary>Writes the table as an 8-bit VTF.</summary>
    public static void Write(string path)
    {
        var rgba = new byte[Size * Size * 4];
        for (var row = 0; row < Size; row++)
        {
            for (var column = 0; column < Size; column++)
            {
                var (a, b) = Integrate(column, row);
                var offset = ((row * Size) + column) * 4;
                rgba[offset] = (byte)Math.Round(Math.Clamp(a, 0f, 1f) * 255f);
                rgba[offset + 1] = (byte)Math.Round(Math.Clamp(b, 0f, 1f) * 255f);
                rgba[offset + 3] = 255;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SpriteVtfWriter.Write(path, rgba, Size, Size, 1, 1, null, clampU: true, clampV: true);
    }

    private static double RadicalInverse(uint bits)
    {
        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        return bits * 2.3283064365386963e-10;
    }
}
