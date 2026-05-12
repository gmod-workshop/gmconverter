using GMConverter.Common;

namespace GMConverter.Explorer;

internal sealed class ExplorerService
{
    public const string AutoProfileId = "auto";

    private readonly IReadOnlyList<IExplorer> explorers =
    [
        new MOWExplorer(),
        new GenericExplorer()
    ];

    public IReadOnlyList<ExplorerProfile> Profiles { get; }

    public ExplorerService()
    {
        Profiles = explorers
            .Select(explorer => new ExplorerProfile(explorer.Id, explorer.DisplayName))
            .ToArray();
    }

    public ExplorerResolvedEntry ResolveEntry(ExplorerFileEntry fileEntry)
    {
        var explorer = ResolveEntryExplorer(fileEntry);
        return explorer.ResolveEntry(fileEntry);
    }

    public void ClearCaches()
    {
        foreach (var explorer in explorers)
        {
            explorer.ClearCaches();
        }
    }

    public ExplorerScanResult Scan(string rootPath, string profileId)
    {
        var target = new ExplorerTarget(rootPath);
        if (!target.IsDirectory && !target.IsFile)
        {
            throw new GMConverterException($"Explorer path not found: {target.FullPath}");
        }

        var explorer = ResolveExplorer(target, profileId);
        var entries = explorer.Scan(target)
            .OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ExplorerScanResult(
            new ExplorerProfile(explorer.Id, explorer.DisplayName),
            entries);
    }

    private IExplorer ResolveExplorer(ExplorerTarget target, string profileId)
    {
        if (!string.Equals(profileId, AutoProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return explorers.FirstOrDefault(explorer => string.Equals(explorer.Id, profileId, StringComparison.OrdinalIgnoreCase))
                ?? throw new GMConverterException($"Unsupported explorer: {profileId}");
        }

        return explorers.FirstOrDefault(explorer => explorer.Supports(target))
            ?? throw new GMConverterException($"No explorer supports: {target.FullPath}");
    }

    private IExplorer ResolveEntryExplorer(ExplorerFileEntry fileEntry)
    {
        if (!string.IsNullOrWhiteSpace(fileEntry.ExplorerId) &&
            explorers.FirstOrDefault(explorer => string.Equals(explorer.Id, fileEntry.ExplorerId, StringComparison.OrdinalIgnoreCase)) is { } explorer)
        {
            return explorer;
        }

        return explorers.FirstOrDefault(explorer => string.Equals(explorer.Id, GenericExplorerId, StringComparison.OrdinalIgnoreCase))
            ?? throw new GMConverterException("Generic explorer is unavailable.");
    }

    private static string GenericExplorerId => "generic";
}
