using GMConverter.SDK.Explorer;

namespace GMConverter.SDK.Profiles;

/// <summary>
/// A game GMConverter knows about: one assets are converted from (<see cref="IImportGameProfile"/>),
/// one they are converted for (<see cref="IExportGameProfile"/>), or both. The host lists
/// registered profiles, detects which game a folder holds, and passes the chosen game's
/// <see cref="Id"/> on (<see cref="ExplorerTarget.GameId"/>, <see cref="ExplorerFileEntry.GameId"/>,
/// <see cref="Exporters.ExportContext.TargetGameId"/>). Only identity, detection and neutral model
/// changes are shared; what a plugin does differently for a game stays inside the plugin, looked
/// up by <see cref="Id"/>.
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
