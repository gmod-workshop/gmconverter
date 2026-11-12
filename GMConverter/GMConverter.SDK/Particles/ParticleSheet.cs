namespace GMConverter.SDK.Particles;

/// <summary>
/// A flipbook: the emitter's texture is a grid of frames, read left to right then top to bottom.
/// </summary>
/// <param name="Columns">Frames across.</param>
/// <param name="Rows">Frames down.</param>
/// <param name="FrameOverLife">
/// The frame (0 is the first) through each particle's life, 0 to 1; null shows one random frame per particle.
/// </param>
public sealed record ParticleSheet(int Columns, int Rows, ParticleFloat? FrameOverLife)
{
    /// <summary>How many frames the sheet holds.</summary>
    public int FrameCount => Columns * Rows;
}
