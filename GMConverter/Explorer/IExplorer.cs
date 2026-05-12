namespace GMConverter.Explorer;

internal interface IExplorer
{
    string Id { get; }

    string DisplayName { get; }

    bool Supports(ExplorerTarget target);

    IReadOnlyList<ExplorerFileEntry> Scan(ExplorerTarget target);

    ExplorerResolvedEntry ResolveEntry(ExplorerFileEntry fileEntry);

    void ClearCaches();
}
