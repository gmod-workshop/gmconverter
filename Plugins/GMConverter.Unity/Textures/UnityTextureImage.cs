namespace GMConverter.Unity.Textures;

/// <summary>A decoded texture: 8-bit RGBA pixels, rows from the top of the image down.</summary>
internal sealed record UnityTextureImage(string Name, int Width, int Height, byte[] Rgba);
