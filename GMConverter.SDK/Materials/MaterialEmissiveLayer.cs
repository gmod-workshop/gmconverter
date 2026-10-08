using System.Numerics;
using GMConverter.SDK.Textures;

namespace GMConverter.SDK.Materials;

/// <summary>
/// Animated glow whose colour scrolls under a fixed mask: emission is <see cref="Color"/> sampled
/// at the mesh UVs offset by <see cref="ScrollRate"/> x time, times <see cref="Mask"/>'s alpha
/// sampled at the unscrolled UVs. ScrollRate uses the same image-space units as
/// <see cref="Material.UvScrollRate"/>. Exporters that can't animate it use the material's
/// static <see cref="Material.EmissiveTexture"/> instead.
/// </summary>
public sealed record MaterialEmissiveLayer(Texture Color, Texture Mask, Vector2 ScrollRate);
