using GMConverter.UnrealEngine.Formats.PSA;

namespace GMConverter.UnrealEngine.Archives;

/// <summary>A blend space as read from the archives, before its samples are exported.</summary>
internal sealed record UnrealBlendSpace(
    string Name,
    bool IsAimOffset,
    IReadOnlyList<PSABlendSpaceAxis> Axes,
    IReadOnlyList<UnrealBlendSpaceSample> Samples);
