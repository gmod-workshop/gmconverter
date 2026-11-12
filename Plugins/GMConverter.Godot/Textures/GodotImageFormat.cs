namespace GMConverter.Godot.Textures;

/// <summary>Godot's <c>Image.Format</c> values for the formats the texture reader decodes.</summary>
internal enum GodotImageFormat
{
    L8 = 0,
    La8 = 1,
    R8 = 2,
    Rg8 = 3,
    Rgb8 = 4,
    Rgba8 = 5,
    Dxt1 = 17,
    Dxt3 = 18,
    Dxt5 = 19,
    RgtcR = 20,
    RgtcRg = 21,
    BptcRgba = 22,
    BptcRgbf = 23,
    BptcRgbfu = 24,

    /// <summary>A normal map compressed as DXT5, its X kept in red and its Y in alpha.</summary>
    Dxt5RaAsRg = 34,
}
