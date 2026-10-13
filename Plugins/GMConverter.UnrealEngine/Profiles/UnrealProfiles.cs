using GMConverter.SDK.Explorer;
using GMConverter.SDK.Profiles;
using GMConverter.UnrealEngine.Profiles.Fortnite;
using GMConverter.UnrealEngine.Profiles.ZeroCompany;

namespace GMConverter.UnrealEngine.Profiles;

/// <summary>The Unreal games this plugin knows, and the choice of one for an archive folder.</summary>
internal static class UnrealProfiles
{
    private static readonly GenericUnrealProfile _generic = new();

    /// <summary>Every profile, specific games first. The plugin registers these with the host.</summary>
    public static IReadOnlyList<IUnrealProfile> All { get; } =
    [
        new FortniteProfile(),
        new ZeroCompanyProfile(),
        _generic,
    ];

    /// <summary>
    /// The profile for <paramref name="gameId"/> when it names one of this plugin's games;
    /// otherwise the profile that best detects <paramref name="archiveDirectory"/>, falling back to
    /// the generic Unreal profile. A game id from another plugin is ignored.
    /// </summary>
    public static IUnrealProfile Select(string archiveDirectory, string? gameId)
    {
        if (gameId is not null &&
            All.FirstOrDefault(profile => string.Equals(profile.Id, gameId, StringComparison.OrdinalIgnoreCase)) is { } chosen)
        {
            return chosen;
        }

        var target = new ExplorerTarget(archiveDirectory);
        return All
            .Select(profile => (Profile: profile, Match: profile.Detect(target)))
            .Where(candidate => candidate.Match > GameMatch.None)
            .OrderByDescending(candidate => candidate.Match)
            .Select(candidate => candidate.Profile)
            .FirstOrDefault() ?? _generic;
    }
}
