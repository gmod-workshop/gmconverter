using GMConverter.SDK.Animation;
using GMConverter.SDK.Explorer;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Options;
using GMConverter.SDK.Profiles;
using GMConverter.SourceEngine.Animation;

namespace GMConverter.SourceEngine.Profiles;

/// <summary>
/// Any other Source game: plain MDL output with nothing game-specific added beyond the NPC
/// activities every Source game's NPC code shares.
/// </summary>
internal sealed class GenericSourceProfile : ISourceProfile
{
    public string Id => "source";

    public string DisplayName => "Other Source game";

    public string Engine => "Source";

    public string ExporterFormat => "mdl";

    public OptionSchema OptionSchema => OptionSchema.Empty;

    public IReadOnlyList<SourceActivity> GetActivities(Model model, AnimationClip clip, OptionValues options)
    {
        if (clip.Role is not { } role || clip.Layering is not null)
        {
            return [];
        }

        var activity = model.FirstPerson ? SourceActivities.ViewModel(role) : SourceActivities.Npc(role);
        return activity is null ? [] : [new SourceActivity(activity)];
    }

    /// <summary>A fallback for any folder holding a Source <c>gameinfo.txt</c>.</summary>
    public GameMatch Detect(ExplorerTarget target)
    {
        return target.IsDirectory && File.Exists(Path.Join(target.FullPath, "gameinfo.txt")) ? GameMatch.Fallback : GameMatch.None;
    }

    public IReadOnlyList<SourceAnimationEvent> GetEvents(AnimationClip clip, OptionValues options) => SourceAnimationEvents.For(clip);

    public SourcePoseParameter? GetPoseParameter(BlendAxis axis) => SourcePoseParameters.For(axis);
}
