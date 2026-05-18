namespace GMConverter.Explorer;

internal sealed record ExplorerResolvedEntry(
    string InputPath,
    string MaterialDirectory,
    string? AnimationPath = null,
    string? Details = null);
