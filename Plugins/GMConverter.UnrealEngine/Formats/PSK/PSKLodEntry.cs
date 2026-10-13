namespace GMConverter.UnrealEngine.Formats.PSK;

/// <summary>One lower level of detail of a mesh.</summary>
/// <param name="Path">The level's own PSK file, relative to the main mesh's folder.</param>
/// <param name="ScreenSize">Unreal's screen size at which the level takes over.</param>
internal sealed record PSKLodEntry(string Path, float ScreenSize);
