namespace GMConverter.SDK.Explorer;

/// <summary>
/// A model or animation asset an explorer found while scanning. <c>SkeletonId</c> identifies the
/// skeleton a skinned mesh or animation is authored against, when the explorer can tell;
/// animations with the same id as a mesh can be bundled onto it. <c>GameId</c> is the game the
/// entry was scanned as (see <see cref="ExplorerTarget.GameId"/>); the host fills it in after a
/// scan, so explorers resolving the entry can apply the same game's settings.
/// <para>
/// <c>Kind</c> says whether the entry is a model or an animation asset. <c>CanAddToModel</c> marks
/// animation assets the explorer can resolve into animations for a model (the host's Add
/// Animations), and <c>BundlesWithModel</c> marks those the host adds automatically when it
/// bundles every animation sharing a mesh's skeleton; wrappers that repeat other clips leave it off.
/// </para>
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
    string? GameId = null,
    ExplorerEntryKind Kind = ExplorerEntryKind.Model,
    bool CanAddToModel = false,
    bool BundlesWithModel = false);
