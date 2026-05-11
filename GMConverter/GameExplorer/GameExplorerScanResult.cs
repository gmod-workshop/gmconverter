namespace GMConverter.GameExplorer;

internal sealed record GameExplorerScanResult(
    GameProfile Profile,
    IReadOnlyList<GameExplorerEntry> Entries);
