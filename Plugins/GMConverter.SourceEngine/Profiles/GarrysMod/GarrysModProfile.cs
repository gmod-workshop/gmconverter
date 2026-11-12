using GMConverter.SDK.Animation;
using GMConverter.SDK.Explorer;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Options;
using GMConverter.SDK.Profiles;
using GMConverter.SourceEngine.Animation;
using GMConverter.SourceEngine.Tools;

namespace GMConverter.SourceEngine.Profiles.GarrysMod;

/// <summary>
/// Garry's Mod, the MDL exporter's default target. It adds the attachments Garry's Mod code looks
/// up by name, and gives sequences the NPC activities, the player (<c>ACT_HL2MP_*</c>) activities,
/// or both, depending on what the model is for. First-person view models get the view model
/// (<c>ACT_VM_*</c>) activities instead.
/// </summary>
internal sealed class GarrysModProfile : ISourceProfile
{
    public const string HandAttachmentsOption = "handAttachments";
    public const string EyesOption = "eyes";
    public const string CharacterUseOption = "characterUse";
    public const string NpcUse = "npc";
    public const string PlayerUse = "player";
    public const string BothUses = "both";
    public const string HandsOption = "hands";
    public const string ModelHands = "model";
    public const string PlayerHands = "player";
    public const string WorldModelOption = "worldModel";
    public const string ParticleEffectsOption = "particleEffects";
    public const string PcfEffects = "pcf";
    public const string GmfxEffects = "gmfx";
    public const string MaterialsOption = "materials";
    public const string VmtMaterials = "vmt";
    public const string GmfxMaterials = "gmfx";
    public const string ShaderKitPathOption = "shaderKitPath";

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
            new OptionDescriptor(HandsOption, OptionType.Enum, "View model hands")
            {
                DefaultValue = ModelHands,
                Choices = [ModelHands, PlayerHands],
                Description = "For first-person view models: keep the model's own arms (a v_ model, SWEP.UseHands = false), or move its animations onto the player's hands and keep only what they hold (a c_ model, SWEP.UseHands = true).",
            },
            new OptionDescriptor(WorldModelOption, OptionType.Bool, "World model")
            {
                DefaultValue = false,
                Description = "For first-person view models: write the world model instead (a w_ model for SWEP.WorldModel): only what the right hand holds, on a ValveBiped.Bip01_R_Hand bone that Garry's Mod matches to the player's hand.",
            },
            new OptionDescriptor(ParticleEffectsOption, OptionType.Enum, "Particle effects")
            {
                DefaultValue = PcfEffects,
                Choices = [PcfEffects, GmfxEffects],
                Description = "How the model's animations start their particle effects: as Source particle events for effects exported as pcf, or as a client Lua file that starts effects exported as gmfx (GMConverter's shader effects).",
            },
            new OptionDescriptor(MaterialsOption, OptionType.Enum, "Materials")
            {
                DefaultValue = VmtMaterials,
                Choices = [VmtMaterials, GmfxMaterials],
                Description = "How the model's materials are lit: by Source's VertexLitGeneric (vmt), or, where the source game's shading is known, by shaders that light them as it does, which the gmfx client Lua applies over the VertexLitGeneric materials (gmfx).",
            },
            new OptionDescriptor(ShaderKitPathOption, OptionType.Path, "Shader kit")
            {
                Description = "For gmfx materials: the folder with ShaderCompile.exe and the Garry's Mod shader headers (meetric1/gmod_shader_guide). Auto-downloads to tools/ when omitted.",
                DefaultValueFactory = ShaderCompiler.TryFindLocalDefault,
            },
            new OptionDescriptor(EyesOption, OptionType.Bool, "Eyes")
            {
                DefaultValue = true,
                Description = "Add an eyes attachment on the head and set $eyeposition from it, so NPCs look and aim from their head.",
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
        // A view model is the player's own arms: NPC eyes and held-item hand attachments don't apply.
        if (model.FirstPerson)
        {
            // A world model is the held item placed in Garry's Mod's hand, as the c_ model holds it.
            if (options.GetBool(WorldModelOption, defaultValue: false))
            {
                return HumanoidRetargeter.ExtractHeld(HumanoidRetargeter.RetargetArms(model, GarrysModHands.Skeleton), HumanoidBone.RightHand, GarrysModHands.RightHandle);
            }

            return options.GetString(HandsOption) == PlayerHands ? HumanoidRetargeter.RetargetArms(model, GarrysModHands.Skeleton) : model;
        }

        if (options.GetBool(HandAttachmentsOption, defaultValue: true))
        {
            model = HandAttachments.AddAliases(model);
        }

        return options.GetBool(EyesOption, defaultValue: true) ? EyesAttachment.AddIfMissing(model) : model;
    }

    public IReadOnlyList<SourceActivity> GetActivities(Model model, AnimationClip clip, OptionValues options)
    {
        // Layers are delta sequences played on top of others, never chosen by activity.
        if (clip.Role is not { } role || clip.Layering is not null)
        {
            return [];
        }

        // Weapon code (SWEPs included) plays a view model's sequences by ACT_VM_* activity.
        if (model.FirstPerson)
        {
            return SourceActivities.ViewModel(role) is { } viewModel ? [new SourceActivity(viewModel)] : [];
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

    // Effects exported as gmfx are started by the Lua file the MDL exporter writes, not by QC events.
    public IReadOnlyList<SourceAnimationEvent> GetEvents(AnimationClip clip, OptionValues options) =>
        SourceAnimationEvents.For(clip, includeParticleEffects: options.GetString(ParticleEffectsOption) != GmfxEffects);

    public SourcePoseParameter? GetPoseParameter(BlendAxis axis) => SourcePoseParameters.For(axis);
}
