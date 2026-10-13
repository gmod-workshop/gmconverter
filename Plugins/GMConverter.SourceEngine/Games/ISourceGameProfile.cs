using GMConverter.SDK.Animation;
using GMConverter.SDK.Games;
using GMConverter.SDK.Options;

namespace GMConverter.SourceEngine.Games;

/// <summary>
/// A Source game the MDL exporter can target, with what the exporter does differently for it.
/// Identity, options and neutral model changes come from <see cref="IExportGameProfile"/>; the
/// exporter finds the profile by <see cref="SDK.Exporters.ExportContext.TargetGameId"/> through
/// <see cref="SourceGameProfiles"/>.
/// </summary>
internal interface ISourceGameProfile : IExportGameProfile
{
    /// <summary>
    /// The activities to give a clip's sequences, from its role. Empty for clips without a role or
    /// whose role this game has no activity for.
    /// </summary>
    IReadOnlyList<SourceActivity> GetActivities(AnimationClip clip, OptionValues options);
}
