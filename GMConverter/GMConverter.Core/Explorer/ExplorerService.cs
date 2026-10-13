using GMConverter.Core.Plugins;
using GMConverter.Core.Profiles;
using GMConverter.SDK.Common;
using GMConverter.SDK.Explorer;

namespace GMConverter.Core.Explorer;

internal sealed class ExplorerService
{
    public const string AutoExplorerId = "auto";

    private readonly IReadOnlyList<IExplorer> _explorers;

    public IReadOnlyList<ExplorerInfo> Explorers { get; }

    public ExplorerService()
    {
        // Every engine-specific explorer is contributed by a plugin through
        // PluginHost.Registry.Explorers. The GenericExplorer fallback stays at the very end so
        // plugins get a chance to handle a target before it falls into the generic catch-all.
        List<IExplorer> explorers = [.. PluginHost.Registry.Explorers];
        explorers.Add(new GenericExplorer());
        _explorers = explorers;

        Explorers = [.. _explorers.Select(explorer => new ExplorerInfo(explorer.Id, explorer.DisplayName))];
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

    /// <summary>
    /// Scans <paramref name="rootPath"/> with the explorer <paramref name="explorerId"/> (or the first
    /// that supports it, for <see cref="AutoExplorerId"/>). The game is detected, or taken from
    /// <paramref name="gameId"/> when one is chosen, and passed to the explorer and stamped on
    /// every entry.
    /// </summary>
    public ExplorerScanResult Scan(string rootPath, string explorerId, string? gameId = GameCatalog.AutoId)
    {
        var target = new ExplorerTarget(rootPath);
        if (!target.IsDirectory && !target.IsFile)
        {
            throw new GMConverterException($"Explorer path not found: {target.FullPath}");
        }

        var game = GameCatalog.Resolve(target, gameId);
        target = target with { GameId = game?.Profile.Id };

        var isAuto = string.Equals(explorerId, AutoExplorerId, StringComparison.OrdinalIgnoreCase) && target.IsDirectory;
        var explorer = ResolveExplorer(target, isAuto ? AutoExplorerId : explorerId);
        var entries = explorer.Scan(target)
            .Select(entry => entry.GameId is null && target.GameId is not null ? entry with { GameId = target.GameId } : entry)
            .OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (isAuto && entries.Length == 0)
        {
            throw new GMConverterException($"No explorer supports: {target.FullPath}");
        }

        return new ExplorerScanResult(new ExplorerInfo(explorer.Id, explorer.DisplayName), entries, game);
    }

    private IExplorer ResolveExplorer(ExplorerTarget target, string explorerId)
    {
        if (!string.Equals(explorerId, AutoExplorerId, StringComparison.OrdinalIgnoreCase))
        {
            return _explorers.FirstOrDefault(explorer => string.Equals(explorer.Id, explorerId, StringComparison.OrdinalIgnoreCase))
                ?? throw new GMConverterException($"Unsupported explorer: {explorerId}");
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
