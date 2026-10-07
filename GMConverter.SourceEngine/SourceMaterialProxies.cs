using System.Numerics;
using GMConverter.SDK.Materials;

namespace GMConverter.SourceEngine;

internal static class SourceMaterialProxies
{
    // Material.UvScrollRate is in image space (V down the stored texture), which is also the space
    // Source applies texture transforms in, so the vector maps straight to a TextureScroll rate and
    // angle. The bump transform scrolls with the base so normal detail stays aligned with it.
    public static void WriteUvScroll(StreamWriter writer, Material material)
    {
        if (material.UvScrollRate is not { } scroll || scroll == Vector2.Zero)
        {
            return;
        }

        var rate = scroll.Length();
        var angle = MathF.Atan2(scroll.Y, scroll.X) * (180f / MathF.PI);
        writer.WriteLine("    \"Proxies\"");
        writer.WriteLine("    {");
        WriteTextureScroll(writer, "$basetexturetransform", rate, angle);
        if (material.NormalTexture is not null)
        {
            WriteTextureScroll(writer, "$bumptransform", rate, angle);
        }

        writer.WriteLine("    }");
    }

    private static void WriteTextureScroll(StreamWriter writer, string transformVariable, float rate, float angle)
    {
        writer.WriteLine("        \"TextureScroll\"");
        writer.WriteLine("        {");
        writer.WriteLine(FormattableString.Invariant($"            \"texturescrollvar\" \"{transformVariable}\""));
        writer.WriteLine(FormattableString.Invariant($"            \"texturescrollrate\" \"{rate:0.######}\""));
        writer.WriteLine(FormattableString.Invariant($"            \"texturescrollangle\" \"{angle:0.##}\""));
        writer.WriteLine("        }");
    }
}
