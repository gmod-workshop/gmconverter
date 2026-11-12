using GMConverter.SDK.Textures;

namespace GMConverter.SDK.Materials;

/// <summary>A texture a <see cref="MaterialGraph"/> samples, with how it is read.</summary>
/// <param name="Texture">The image.</param>
/// <param name="Srgb">Whether its colour channels are sRGB-encoded (decoded to linear when sampled); false for data such as masks and noise.</param>
/// <param name="ClampU">Whether coordinates past the edge along U repeat the edge texel rather than wrapping.</param>
/// <param name="ClampV">The same along V.</param>
public sealed record MaterialGraphTexture(Texture Texture, bool Srgb = true, bool ClampU = false, bool ClampV = false);
