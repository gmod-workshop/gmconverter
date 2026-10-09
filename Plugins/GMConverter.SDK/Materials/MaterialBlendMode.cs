namespace GMConverter.SDK.Materials;

// How a material combines with what is behind it. Unspecified keeps the historical behaviour:
// alpha-blended when the diffuse texture has alpha, opaque otherwise.
public enum MaterialBlendMode
{
    Unspecified,

    // Blend by the diffuse alpha.
    AlphaBlend,

    // Cut out pixels whose diffuse alpha is below Material.AlphaCutoff; the rest are opaque.
    AlphaTest,

    // Add the material's colour to what is behind it (glows, energy effects); alpha is ignored.
    Additive
}
