namespace GMConverter.Explorer;

internal sealed record ExplorerFileEntry(
    string DisplayPath,
    string FilePath,
    string InputFormat,
    string MaterialDirectory,
    string SearchRoot,
    string? ArchivePath = null,
    string? ArchiveEntryPath = null,
    string? ExplorerId = null);
