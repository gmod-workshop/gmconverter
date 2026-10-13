using GMConverter.Core.Profiles;
using GMConverter.SDK.Explorer;

namespace GMConverter.Core.Explorer;

/// <summary>What a scan found, which explorer found it, and the game it was scanned as.</summary>
internal sealed record ExplorerScanResult(
    ExplorerInfo Explorer,
    IReadOnlyList<ExplorerFileEntry> Entries,
    GameDetection? Game = null);
