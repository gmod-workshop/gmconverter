using GMConverter.SDK.Materials;

namespace GMConverter.SourceEngine;

internal static class SourceMaterialBlend
{
    // VertexLitGeneric supports one blend at a time: $additive for glows, $alphatest for cut-outs
    // (sorts like an opaque surface), $translucent for true alpha blending.
    public static void Write(StreamWriter writer, Material material)
    {
        switch (material.BlendMode)
        {
            case MaterialBlendMode.Additive:
                writer.WriteLine("    \"$additive\" \"1\"");
                break;
            case MaterialBlendMode.AlphaTest when material.HasAlpha:
                writer.WriteLine("    \"$alphatest\" \"1\"");
                writer.WriteLine(FormattableString.Invariant($"    \"$alphatestreference\" \"{material.AlphaCutoff:0.###}\""));
                break;
            case MaterialBlendMode.Unspecified or MaterialBlendMode.AlphaBlend when material.HasAlpha:
                writer.WriteLine("    \"$translucent\" \"1\"");
                break;
        }
    }
}
