using GMConverter.SDK.Explorer;

namespace GMConverter.Core.Explorer;

internal sealed record ExplorerScanResult(
    ExplorerProfile Profile,
    IReadOnlyList<ExplorerFileEntry> Entries);
