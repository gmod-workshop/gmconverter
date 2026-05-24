namespace GMConverter.SDK.Explorer;

public sealed record ExplorerResolvedEntry(
    string InputPath,
    string MaterialDirectory,
    string? AnimationPath = null,
    string? Details = null);
