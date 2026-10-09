using GMConverter.SDK.Explorer;

namespace GMConverter.Explorer;

internal sealed record ExplorerScanResult(
    ExplorerProfile Profile,
    IReadOnlyList<ExplorerFileEntry> Entries);
