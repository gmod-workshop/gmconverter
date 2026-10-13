namespace GMConverter.UnrealEngine.Archives;

/// <summary>A blend space sample: the sequence's object path (as the asset registry lists it), its name and position.</summary>
internal sealed record UnrealBlendSpaceSample(string ObjectPath, string Name, IReadOnlyList<float> Coordinates);
