namespace GMConverter.UnrealEngine.Formats.PSA;

/// <summary>An animation sequence placed in an Unreal blend space.</summary>
/// <param name="ClipName">The sequence's name, which is also its clip name in the exported PSA.</param>
/// <param name="Coordinates">Its position, one value per axis.</param>
internal sealed record PSABlendSpaceSample(string ClipName, IReadOnlyList<float> Coordinates);
