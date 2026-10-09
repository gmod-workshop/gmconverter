using GMConverter.SDK.Common;
using GMConverter.SDK.Textures;

namespace GMConverter.Core.Geometry;

/// <summary>
/// Pipeline-specific texture transforms exposed as extensions on the SDK <see cref="Texture"/>
/// contract, with the actual work delegated to the host's concrete <see cref="ImageSharpTexture"/>.
/// Keeps call sites that hold a <see cref="Texture"/> reference from having to downcast and lets
/// the SDK remain free of ImageSharp-specific concerns.
/// </summary>
internal static class TextureExtensions
{
    public static ImageSharpTexture WithOpenGlNormalMap(this Texture texture, string? textureName = null)
        => RequireImageSharp(texture).WithOpenGlNormalMap(textureName);

    public static ImageSharpTexture ToGltfMetallicRoughness(this Texture texture, string? textureName = null)
        => RequireImageSharp(texture).ToGltfMetallicRoughness(textureName);

    public static ImageSharpTexture ToSpecularFactorMask(this Texture texture, string? textureName = null)
        => RequireImageSharp(texture).ToSpecularFactorMask(textureName);

    private static ImageSharpTexture RequireImageSharp(Texture texture)
    {
        return texture as ImageSharpTexture
            ?? throw new GMConverterException(
                $"Texture pipeline transforms require an {nameof(ImageSharpTexture)}, got {texture.GetType().FullName}.");
    }
}
