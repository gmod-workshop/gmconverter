using GMConverter.SDK.Animation;
using GMConverter.SDK.Explorer;
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

    public IReadOnlyList<SourceActivity> GetActivities(AnimationClip clip, OptionValues options)
    {
        return clip.Role is { } role && clip.Layering is null && SourceActivities.Npc(role) is { } activity
            ? [new SourceActivity(activity)]
            : [];
    }

    /// <summary>A fallback for any folder holding a Source <c>gameinfo.txt</c>.</summary>
    public GameMatch Detect(ExplorerTarget target)
    {
        return target.IsDirectory && File.Exists(Path.Join(target.FullPath, "gameinfo.txt")) ? GameMatch.Fallback : GameMatch.None;
    }
}
