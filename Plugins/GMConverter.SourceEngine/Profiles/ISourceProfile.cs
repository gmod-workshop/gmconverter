using GMConverter.SDK.Animation;
using GMConverter.SDK.Options;
using GMConverter.SDK.Profiles;
using GMConverter.SourceEngine.Animation;

namespace GMConverter.SourceEngine.Profiles;

/// <summary>
/// A Source game the MDL exporter can target, with what the exporter does differently for it.
/// Identity, options and neutral model changes come from <see cref="IExportGameProfile"/>; the
/// exporter finds the profile by <see cref="SDK.Exporters.ExportContext.TargetGameId"/> through
/// <see cref="SourceProfiles"/>.
/// </summary>
internal interface ISourceProfile : IExportGameProfile
{
    /// <summary>
    /// The activities to give a clip's sequences, from its role. Empty for clips without a role or
    /// whose role this game has no activity for.
    /// </summary>
    IReadOnlyList<SourceActivity> GetActivities(AnimationClip clip, OptionValues options);

    /// <summary>The QC events to write into a clip's sequences, from its animation events.</summary>
    IReadOnlyList<SourceAnimationEvent> GetEvents(AnimationClip clip);

    /// <summary>The pose parameter a blend space axis drives in this game, or null when it has none.</summary>
    SourcePoseParameter? GetPoseParameter(BlendAxis axis);
}
