using GMConverter.SDK.Animation;
using GMConverter.SDK.Explorer;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Options;
using GMConverter.SDK.Profiles;
using GMConverter.SourceEngine.Animation;

namespace GMConverter.SourceEngine.Profiles.GarrysMod;

/// <summary>
/// Garry's Mod, the MDL exporter's default target. It adds the attachments Garry's Mod code looks
/// up by name, and gives sequences the NPC activities, the player (<c>ACT_HL2MP_*</c>) activities,
/// or both, depending on what the model is for.
/// </summary>
internal sealed class GarrysModProfile : ISourceProfile
{
    public const string HandAttachmentsOption = "handAttachments";
    public const string CharacterUseOption = "characterUse";
    public const string NpcUse = "npc";
    public const string PlayerUse = "player";
    public const string BothUses = "both";

    // When a clip needs both sets, its own sequence keeps the NPC activity and a copy with this
    // suffix carries the player one.
    private const string _playerSequenceSuffix = "_player";

    public string Id => "garrysmod";

    public string DisplayName => "Garry's Mod";

    public string Engine => "Source";

    public string ExporterFormat => "mdl";

    public OptionSchema OptionSchema { get; } = new(
    [
        new OptionGroup("garrysmod", "Garry's Mod",
        [
            new OptionDescriptor(CharacterUseOption, OptionType.Enum, "Character use")
            {
                DefaultValue = NpcUse,
                Choices = [NpcUse, PlayerUse, BothUses],
                Description = "Which activities animations get: NPC activities (ACT_IDLE, ACT_RUN, …), player-model activities (ACT_HL2MP_*), or both, with a copy of each sequence for the player set.",
            },
            new OptionDescriptor(HandAttachmentsOption, OptionType.Bool, "Hand attachments")
            {
                DefaultValue = true,
                Description = "Add anim_attachment_RH and anim_attachment_LH on the hands, copied from the model's hand sockets, which Garry's Mod uses to place held items.",
            },
        ]),
    ]);

    /// <summary>Exact for a <c>garrysmod</c> folder holding <c>gameinfo.txt</c>, or a folder containing one.</summary>
    public GameMatch Detect(ExplorerTarget target)
    {
        if (!target.IsDirectory)
        {
            return GameMatch.None;
        }

        var isGameFolder = string.Equals(Path.GetFileName(target.FullPath.TrimEnd(Path.DirectorySeparatorChar)), "garrysmod", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Join(target.FullPath, "gameinfo.txt"));
        return isGameFolder || File.Exists(Path.Join(target.FullPath, "garrysmod", "gameinfo.txt"))
            ? GameMatch.Exact
            : GameMatch.None;
    }

    public Model Prepare(Model model, OptionValues options)
    {
        return options.GetBool(HandAttachmentsOption, defaultValue: true) ? HandAttachments.AddAliases(model) : model;
    }

    public IReadOnlyList<SourceActivity> GetActivities(AnimationClip clip, OptionValues options)
    {
        // Layers are delta sequences played on top of others, never chosen by activity.
        if (clip.Role is not { } role || clip.Layering is not null)
        {
            return [];
        }

        var use = options.GetString(CharacterUseOption) ?? NpcUse;
        List<SourceActivity> activities = [];
        if (use is NpcUse or BothUses && SourceActivities.Npc(role) is { } npc)
        {
            activities.Add(new SourceActivity(npc));
        }

        if (use is PlayerUse or BothUses && SourceActivities.Player(role) is { } player)
        {
            activities.Add(new SourceActivity(player, activities.Count == 0 ? string.Empty : _playerSequenceSuffix));
        }

        return activities;
    }
}
