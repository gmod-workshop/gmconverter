using GMConverter.SDK.Textures;

namespace GMConverter.SDK.Materials;

/// <summary>
/// A second texture combined with the diffuse before lighting (e.g. a lightmap multiplied over a
/// tiled base, or an animated overlay added to it), sampled with the mesh UVs through its own
/// optional transform.
/// </summary>
public sealed record MaterialLayer(
    Texture Texture,
    MaterialLayerBlend Blend,
    MaterialUvTransform? UvTransform = null);
