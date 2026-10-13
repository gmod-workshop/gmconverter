namespace GMConverter.SDK.Explorer;

/// <summary>
/// A model or animation asset an explorer found while scanning. <c>SkeletonId</c> identifies the
/// skeleton a skinned mesh or animation is authored against, when the explorer can tell;
/// animations with the same id as a mesh can be bundled onto it. <c>GameId</c> is the game the
/// entry was scanned as (see <see cref="ExplorerTarget.GameId"/>); the host fills it in after a
/// scan, so explorers resolving the entry can apply the same game's settings.
/// </summary>
public sealed record ExplorerFileEntry(
    string DisplayPath,
    string FilePath,
    string InputFormat,
    string MaterialDirectory,
    string SearchRoot,
    string? ArchivePath = null,
    string? ArchiveEntryPath = null,
    string? ExplorerId = null,
    string? Details = null,
    bool IsConvertible = true,
    string? AssetClass = null,
    string? SkeletonId = null,
    string? GameId = null);
