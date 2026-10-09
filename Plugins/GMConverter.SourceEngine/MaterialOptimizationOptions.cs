namespace GMConverter.SourceEngine;

/// <summary>
/// Controls Source material compile texture optimizations. <see cref="MaxTextureSize"/> caps
/// the longest edge before VTF compile (0 disables resizing); <see cref="DeduplicateTextures"/>
/// hashes resized texture content and reuses an existing VTF when materials end up with byte-
/// identical maps (common after extracting Fortnite spec masks where many materials produce the
/// same phong-exponent output).
/// </summary>
internal sealed record MaterialOptimizationOptions(int MaxTextureSize, bool DeduplicateTextures)
{
    public static MaterialOptimizationOptions Default { get; } = new(0, false);
}
