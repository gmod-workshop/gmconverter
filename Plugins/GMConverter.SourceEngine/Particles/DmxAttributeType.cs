namespace GMConverter.SourceEngine.Particles;

/// <summary>The DMX attribute types particle files use; an array type is its element type plus <see cref="ArrayOffset"/>.</summary>
internal enum DmxAttributeType : byte
{
    Element = 1,
    Int = 2,
    Float = 3,
    Bool = 4,
    String = 5,
    Color = 8,
    Vector3 = 10,
    ElementArray = Element + ArrayOffset,

    /// <summary>What an array type adds to its element type.</summary>
    ArrayOffset = 14,
}
