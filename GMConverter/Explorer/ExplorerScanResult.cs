namespace GMConverter.Explorer;

internal sealed record ExplorerScanResult(
    ExplorerProfile Profile,
    IReadOnlyList<ExplorerFileEntry> Entries);
