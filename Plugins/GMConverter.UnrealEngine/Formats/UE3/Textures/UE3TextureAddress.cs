namespace GMConverter.UnrealEngine.Formats.UE3.Textures;

/// <summary>How a texture is sampled outside the 0-1 range along one axis (UE3's TextureAddress).</summary>
internal enum UE3TextureAddress
{
    Wrap,
    Clamp,

    /// <summary>Every other tile is flipped, so one painted half can cover a symmetric model's two sides.</summary>
    Mirror
}
