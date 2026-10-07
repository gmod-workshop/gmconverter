using System.Numerics;
using GMConverter.SDK.Materials;

namespace GMConverter.SourceEngine;

internal static class SourceMaterialProxies
{
    // Material scroll rates are in image space (V down the stored texture), which is also the space
    // Source applies texture transforms in, so each vector maps straight to a TextureScroll rate and
    // angle. The bump transform scrolls with the base so normal detail stays aligned with it; a
    // separately scrolling emissive layer is the detail layer (see SourceMaterialEmission).
    public static void WriteUvScroll(StreamWriter writer, Material material)
    {
        var baseScroll = material.UvScrollRate is { } scroll && scroll != Vector2.Zero ? scroll : (Vector2?)null;
        var detailScroll = SourceMaterialEmission.UsesDetailLayer(material) ? material.EmissiveUvScrollRate : null;
        if (baseScroll is null && detailScroll is null)
        {
            return;
        }

        writer.WriteLine("    \"Proxies\"");
        writer.WriteLine("    {");
        if (baseScroll is { } baseRate)
        {
            WriteTextureScroll(writer, "$basetexturetransform", baseRate);
            if (material.NormalTexture is not null)
            {
                WriteTextureScroll(writer, "$bumptransform", baseRate);
            }
        }

        if (detailScroll is { } detailRate)
        {
            WriteTextureScroll(writer, "$detailtexturetransform", detailRate);
        }

        writer.WriteLine("    }");
    }

    private static void WriteTextureScroll(StreamWriter writer, string transformVariable, Vector2 scroll)
    {
        var rate = scroll.Length();
        var angle = MathF.Atan2(scroll.Y, scroll.X) * (180f / MathF.PI);
        writer.WriteLine("        \"TextureScroll\"");
        writer.WriteLine("        {");
        writer.WriteLine(FormattableString.Invariant($"            \"texturescrollvar\" \"{transformVariable}\""));
        writer.WriteLine(FormattableString.Invariant($"            \"texturescrollrate\" \"{rate:0.######}\""));
        writer.WriteLine(FormattableString.Invariant($"            \"texturescrollangle\" \"{angle:0.##}\""));
        writer.WriteLine("        }");
    }
}
