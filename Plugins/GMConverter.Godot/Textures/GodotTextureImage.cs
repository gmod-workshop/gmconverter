namespace GMConverter.Godot.Textures;

/// <summary>
/// A decoded texture: RGBA8 pixels, top row first. <c>IsNormalMap</c> is true when the texture was
/// compressed as a normal map, whose X and Y are now in red and green.
/// </summary>
internal sealed record GodotTextureImage(string Name, int Width, int Height, byte[] Rgba, bool IsNormalMap = false);
