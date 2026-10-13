using GMConverter.Core.Games;
using GMConverter.SDK.Explorer;

namespace GMConverter.Core.Explorer;

/// <summary>What a scan found, which explorer found it, and the game it was scanned as.</summary>
internal sealed record ExplorerScanResult(
    ExplorerProfile Profile,
    IReadOnlyList<ExplorerFileEntry> Entries,
    GameDetection? Game = null);
