namespace GMConverter.GameExplorer;

internal sealed record GameExplorerEntry(
    string DisplayPath,
    string FilePath,
    string InputFormat,
    string MaterialDirectory,
    string SearchRoot,
    string? ArchivePath = null,
    string? ArchiveEntryPath = null);
