using GMConverter.Core.Plugins;
using GMConverter.SDK.Common;
using GMConverter.SDK.Explorer;
using GMConverter.SDK.Profiles;

namespace GMConverter.Core.Profiles;

/// <summary>
/// The games plugins registered: games to convert from, which can be detected from a folder, and
/// games to convert for, which are targets of one exporter.
/// </summary>
internal static class GameCatalog
{
    /// <summary>The id that asks the host to detect the game instead of using a chosen one.</summary>
    public const string AutoId = "auto";

    /// <summary>Games to convert from, grouped by engine and then sorted by name.</summary>
    public static IReadOnlyList<IImportGameProfile> ImportProfiles => [.. PluginHost.Registry.GameProfiles
        .OfType<IImportGameProfile>()
        .OrderBy(profile => profile.Engine, StringComparer.OrdinalIgnoreCase)
        .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The games <paramref name="exporterFormat"/> can export for, in registration order; the first
    /// is the default target.
    /// </summary>
    public static IReadOnlyList<IExportGameProfile> TargetsFor(string exporterFormat)
    {
        return [.. PluginHost.Registry.GameProfiles
            .OfType<IExportGameProfile>()
            .Where(profile => string.Equals(profile.ExporterFormat, exporterFormat, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// The target <paramref name="targetId"/> of <paramref name="exporterFormat"/>, or its default
    /// target when <paramref name="targetId"/> is empty. Null when the exporter has no targets.
    /// </summary>
    public static IExportGameProfile? ResolveTarget(string exporterFormat, string? targetId)
    {
        var targets = TargetsFor(exporterFormat);
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return targets.Count > 0 ? targets[0] : null;
        }

        return targets.FirstOrDefault(target => string.Equals(target.Id, targetId, StringComparison.OrdinalIgnoreCase))
            ?? throw new GMConverterException(targets.Count == 0
                ? $"{exporterFormat} output has no target games."
                : $"Unknown target game for {exporterFormat} output: {targetId}. Targets: {string.Join(", ", targets.Select(target => target.Id))}.");
    }

    public static IGameProfile? Find(string id)
    {
        return PluginHost.Registry.GameProfiles.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The strongest claim any game to convert from makes on <paramref name="target"/>, or null
    /// when none claims it. Ties go to the profile registered first. A profile that throws while
    /// detecting is skipped, so one broken plugin cannot stop a scan.
    /// </summary>
    public static GameDetection? Detect(ExplorerTarget target)
    {
        GameDetection? best = null;
        foreach (var profile in PluginHost.Registry.GameProfiles.OfType<IImportGameProfile>())
        {
            var match = TryDetect(profile, target);
            if (match > (best?.Match ?? GameMatch.None))
            {
                best = new GameDetection(profile, match, IsDetected: true);
            }
        }
        return best;
    }

    /// <summary>
    /// Detects the game when <paramref name="gameId"/> is <see cref="AutoId"/> or empty; otherwise
    /// returns the chosen game.
    /// </summary>
    public static GameDetection? Resolve(ExplorerTarget target, string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId) || string.Equals(gameId, AutoId, StringComparison.OrdinalIgnoreCase))
        {
            return Detect(target);
        }

        var profile = Find(gameId) as IImportGameProfile ?? throw new GMConverterException($"Unknown game: {gameId}");
        return new GameDetection(profile, GameMatch.Exact, IsDetected: false);
    }

    private static GameMatch TryDetect(IGameProfile profile, ExplorerTarget target)
    {
        try
        {
            return profile.Detect(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or GMConverterException)
        {
            PerfTimer.Log("games", $"{profile.Id} detection failed: {ex.Message}");
            return GameMatch.None;
        }
    }
}
