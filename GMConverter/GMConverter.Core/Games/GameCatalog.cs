using GMConverter.Core.Plugins;
using GMConverter.SDK.Common;
using GMConverter.SDK.Explorer;
using GMConverter.SDK.Games;

namespace GMConverter.Core.Games;

/// <summary>The games plugins registered, and detection of which one a folder holds.</summary>
internal static class GameCatalog
{
    /// <summary>The id that asks the host to detect the game instead of using a chosen one.</summary>
    public const string AutoId = "auto";

    /// <summary>Registered games, grouped by engine and then sorted by name.</summary>
    public static IReadOnlyList<IGameProfile> Profiles => [.. PluginHost.Registry.GameProfiles
        .OrderBy(profile => profile.Engine, StringComparer.OrdinalIgnoreCase)
        .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)];

    public static IGameProfile? Find(string id)
    {
        return PluginHost.Registry.GameProfiles.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The strongest claim any registered game makes on <paramref name="target"/>, or null when
    /// none claims it. Ties go to the profile registered first. A profile that throws while
    /// detecting is skipped, so one broken plugin cannot stop a scan.
    /// </summary>
    public static GameDetection? Detect(ExplorerTarget target)
    {
        GameDetection? best = null;
        foreach (var profile in PluginHost.Registry.GameProfiles)
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

        var profile = Find(gameId) ?? throw new GMConverterException($"Unknown game: {gameId}");
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
