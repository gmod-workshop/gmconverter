using GMConverter.Common;

namespace GMConverter.Explorer;

internal sealed class ExplorerService
{
    public const string AutoProfileId = "auto";

    private readonly IReadOnlyList<IExplorer> _explorers =
    [
        new UE4Explorer(),
        new MOWExplorer(),
        new UE2Explorer(),
        new GenericExplorer()
    ];

    public IReadOnlyList<ExplorerProfile> Profiles { get; }

    public ExplorerService()
    {
        Profiles = _explorers
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
        foreach (var explorer in _explorers)
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

        if (string.Equals(profileId, AutoProfileId, StringComparison.OrdinalIgnoreCase) && target.IsDirectory)
        {
            return ScanAuto(target);
        }

        var explorer = ResolveExplorer(target, profileId);
        var entries = explorer.Scan(target)
            .OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ExplorerScanResult(
            new ExplorerProfile(explorer.Id, explorer.DisplayName),
            entries);
    }

    private ExplorerScanResult ScanAuto(ExplorerTarget target)
    {
        var explorer = ResolveExplorer(target, AutoProfileId);
        var entries = explorer.Scan(target)
            .OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (entries.Length == 0)
        {
            throw new GMConverterException($"No explorer supports: {target.FullPath}");
        }

        return new ExplorerScanResult(new ExplorerProfile(explorer.Id, explorer.DisplayName), entries);
    }

    private IExplorer ResolveExplorer(ExplorerTarget target, string profileId)
    {
        if (!string.Equals(profileId, AutoProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return _explorers.FirstOrDefault(explorer => string.Equals(explorer.Id, profileId, StringComparison.OrdinalIgnoreCase))
                ?? throw new GMConverterException($"Unsupported explorer: {profileId}");
        }

        return _explorers.FirstOrDefault(explorer => explorer.Supports(target))
            ?? throw new GMConverterException($"No explorer supports: {target.FullPath}");
    }

    private IExplorer ResolveEntryExplorer(ExplorerFileEntry fileEntry)
    {
        if (!string.IsNullOrWhiteSpace(fileEntry.ExplorerId) &&
            _explorers.FirstOrDefault(explorer => string.Equals(explorer.Id, fileEntry.ExplorerId, StringComparison.OrdinalIgnoreCase)) is { } explorer)
        {
            return explorer;
        }

        return _explorers.FirstOrDefault(explorer => string.Equals(explorer.Id, GenericExplorerId, StringComparison.OrdinalIgnoreCase))
            ?? throw new GMConverterException("Generic explorer is unavailable.");
    }

    private static string GenericExplorerId => "generic";
}
