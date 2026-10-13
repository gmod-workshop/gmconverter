using GMConverter.SDK.Explorer;

namespace GMConverter.SDK.Games;

/// <summary>
/// A game GMConverter knows about. The host lists registered profiles, detects which one a folder
/// holds, and passes the chosen profile's <see cref="Id"/> to explorers through
/// <see cref="ExplorerTarget.GameId"/> and <see cref="ExplorerFileEntry.GameId"/>. Only identity
/// and detection are shared here; what a plugin does differently for a game stays inside the
/// plugin, looked up by <see cref="Id"/>.
/// </summary>
public interface IGameProfile
{
    /// <summary>Stable identifier, e.g. <c>"swzc"</c>.</summary>
    string Id { get; }

    /// <summary>Human-readable name shown in the UI, e.g. <c>"Star Wars Zero Company"</c>.</summary>
    string DisplayName { get; }

    /// <summary>The engine the game runs on, used to group games in the UI, e.g. <c>"Unreal Engine 5"</c>.</summary>
    string Engine { get; }

    /// <summary>
    /// How sure this profile is that <paramref name="target"/> holds its game. The host calls this
    /// on every profile for each scan, so it must be cheap: file-system checks only, no archive
    /// mounting.
    /// </summary>
    GameMatch Detect(ExplorerTarget target);
}
