using System.Numerics;
using GMConverter.SDK.Materials;

namespace GMConverter.SourceEngine;

internal static class SourceMaterialEmission
{
    // VertexLitGeneric samples $selfillummask with the base texture's coordinates, so it cannot
    // move independently. An emissive layer with its own scroll is written as an unlit additive
    // detail layer instead ($detailblendmode 5), whose $detailtexturetransform the proxies scroll.
    public static bool UsesDetailLayer(Material material)
    {
        return material.IsIlluminated && material.EmissiveUvScrollRate is { } scroll && scroll != Vector2.Zero;
    }

    public static void Write(StreamWriter writer, Material material, string illumTexturePath)
    {
        if (UsesDetailLayer(material))
        {
            writer.WriteLine(FormattableString.Invariant($"    \"$detail\" \"{illumTexturePath}\""));
            writer.WriteLine("    \"$detailblendmode\" \"5\"");
            writer.WriteLine("    \"$detailscale\" \"1\"");
            return;
        }

        writer.WriteLine("    \"$selfillum\" \"1\"");
        writer.WriteLine(FormattableString.Invariant($"    \"$selfillummask\" \"{illumTexturePath}\""));
    }
}
