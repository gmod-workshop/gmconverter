using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Wwise;
using CUE4Parse.UE4.Wwise;
using CUE4Parse.UE4.Wwise.Enums;
using CUE4Parse.UE4.Wwise.Objects;
using CUE4Parse.UE4.Wwise.Objects.HIRC;
using CUE4Parse.UE4.Wwise.Objects.HIRC.Containers;
using GMConverter.SDK.Common;

namespace GMConverter.UnrealEngine.Audio;

/// <summary>
/// Reads a Wwise event's sound structure from the soundbanks it is cooked with, as a
/// <see cref="WwiseNode"/> tree. A soundbank holds the parents of everything in it, so the
/// actor mixers that set most of a sound's volume and attenuation are read along with it.
/// </summary>
internal sealed class WwiseHierarchyReader
{
    // Property ids as CUE4Parse's EAkPropID numbers them. Older soundbanks number properties
    // differently and CUE4Parse does not translate them, so only banks at least as new as the ones
    // this was checked against (Star Wars Zero Company, bank version 172) are read.
    private const uint _minimumBankVersion = 150;
    private const byte _volumeProperty = (byte)EAkPropID.Volume;
    private const byte _pitchProperty = (byte)EAkPropID.Pitch;
    private const byte _loopProperty = (byte)EAkPropID.Loop;
    private const byte _attenuationProperty = (byte)EAkPropID.AttenuationID;

    // Unreal's Wwise integration passes positions in Unreal units, so attenuation curves are in
    // centimetres.
    private const float _unitsPerMeter = 100f;
    private const int _maxDepth = 32;

    private readonly Dictionary<uint, Hierarchy> _hierarchies;

    private WwiseHierarchyReader(Dictionary<uint, Hierarchy> hierarchies)
    {
        _hierarchies = hierarchies;
    }

    /// <summary>
    /// A reader over the soundbanks <paramref name="eventData"/> is cooked with, or null when none
    /// can be found or read.
    /// </summary>
    public static WwiseHierarchyReader? Load(DefaultFileProvider provider, FWwiseEventCookedData eventData)
    {
        Dictionary<uint, Hierarchy> hierarchies = [];
        foreach (var bank in eventData.SoundBanks)
        {
            var reader = bank.PackagedFile?.BulkData ?? TryReadBankFile(provider, bank.DebugName.Text);
            if (reader?.Hierarchies is not { } bankHierarchies || reader.Header.Version < _minimumBankVersion)
            {
                continue;
            }

            foreach (var hierarchy in bankHierarchies)
            {
                hierarchies.TryAdd(hierarchy.Data.Id, hierarchy);
            }
        }

        return hierarchies.Count == 0 ? null : new WwiseHierarchyReader(hierarchies);
    }

    /// <summary>
    /// What <paramref name="eventId"/> plays, with the mix of the actor mixers above it folded in,
    /// or null when the event is not in the soundbanks or plays nothing this reader understands.
    /// An event with several play actions plays them all at once, so they become a layer.
    /// </summary>
    public WwiseNode? ReadEvent(uint eventId)
    {
        if (!_hierarchies.TryGetValue(eventId, out var hierarchy) || hierarchy.Data is not HierarchyEvent audioEvent)
        {
            return null;
        }

        List<WwiseChild> played = [];
        foreach (var actionId in audioEvent.EventActionIds)
        {
            if (!_hierarchies.TryGetValue(actionId, out var actionHierarchy) ||
                actionHierarchy.Data is not HierarchyEventAction { EventActionType: EAkActionType.Play } action ||
                ReadNode(action.ReferencedId, 0) is not { } node)
            {
                continue;
            }

            var mix = MixOf(action.PropBundle).Then(AncestorMix(action.ReferencedId)).Then(node.Mix);
            played.Add(new WwiseChild(node with { Mix = mix }));
        }

        return played.Count switch
        {
            0 => null,
            1 => played[0].Node,
            _ => new WwiseNode(WwiseNodeKind.Layer, WwiseMix.None, played),
        };
    }

    private WwiseNode? ReadNode(uint id, int depth)
    {
        if (depth > _maxDepth || !_hierarchies.TryGetValue(id, out var hierarchy))
        {
            return null;
        }

        switch (hierarchy.Data)
        {
            case HierarchySoundSfxVoice sound:
                // A source plugin (a tone generator, silence) has no media to export.
                return sound.Source.Plugin.Type == EAkPluginType.Codec
                    ? WwiseNode.Sound(sound.Source.SourceId, MixOf(sound.BaseParams))
                    : null;
            case HierarchyRandomSequenceContainer container:
                {
                    var weights = container.Playlist.ToDictionary(item => item.PlayId, item => (float)item.Weight);
                    var childIds = container.Playlist.Length > 0 ? container.Playlist.Select(item => item.PlayId) : container.ChildIds;
                    var children = ReadChildren(childIds, depth, childId => weights.GetValueOrDefault(childId, 1f));
                    var mix = MixOf(container.BaseParams);
                    // A loop count of zero repeats forever.
                    mix = container.LoopCount == 0 ? mix with { Loops = true } : mix;
                    return children.Count == 0
                        ? null
                        : new WwiseNode(
                            container.Mode == EAkContainerMode.Sequence ? WwiseNodeKind.Sequence : WwiseNodeKind.Random,
                            mix,
                            children,
                            AvoidRepeats: container.AvoidRepeatCount > 0);
                }
            case HierarchySwitchContainer container:
                {
                    // The switch is set by the game at run time; without the game, play its default branch.
                    var package = container.SwitchPackages.FirstOrDefault(item => item.SwitchId == container.DefaultSwitch && item.NodeIds is { Length: > 0 });
                    if (package.NodeIds is not { Length: > 0 })
                    {
                        package = container.SwitchPackages.FirstOrDefault(item => item.NodeIds is { Length: > 0 });
                    }

                    var children = ReadChildren(package.NodeIds ?? container.ChildIds, depth, _ => 1f);
                    return children.Count == 0 ? null : new WwiseNode(WwiseNodeKind.Switch, MixOf(container.BaseParams), children);
                }
            case HierarchyLayerContainer container:
                {
                    var children = ReadChildren(container.ChildIds, depth, _ => 1f);
                    return children.Count == 0 ? null : new WwiseNode(WwiseNodeKind.Layer, MixOf(container.BaseParams), children);
                }
            default:
                return null;
        }
    }

    private List<WwiseChild> ReadChildren(IEnumerable<uint> childIds, int depth, Func<uint, float> weight)
    {
        List<WwiseChild> children = [];
        foreach (var childId in childIds)
        {
            if (ReadNode(childId, depth + 1) is { } child)
            {
                children.Add(new WwiseChild(child, weight(childId)));
            }
        }

        return children;
    }

    // The actor mixers and containers above a played node, outermost first so the attenuation
    // nearest the node wins.
    private WwiseMix AncestorMix(uint id)
    {
        List<WwiseMix> ancestors = [];
        var parentId = BaseOf(id)?.DirectParentId ?? 0;
        for (var depth = 0; parentId != 0 && depth < _maxDepth; depth++)
        {
            if (BaseOf(parentId) is not { } parent)
            {
                break;
            }

            ancestors.Add(MixOf(parent));
            parentId = parent.DirectParentId;
        }

        var mix = WwiseMix.None;
        for (var index = ancestors.Count - 1; index >= 0; index--)
        {
            mix = mix.Then(ancestors[index]);
        }

        return mix;
    }

    private BaseHierarchy? BaseOf(uint id)
    {
        return _hierarchies.TryGetValue(id, out var hierarchy) ? hierarchy.Data switch
        {
            HierarchySoundSfxVoice sound => sound.BaseParams,
            HierarchyRandomSequenceContainer container => container.BaseParams,
            HierarchySwitchContainer container => container.BaseParams,
            HierarchyLayerContainer container => container.BaseParams,
            HierarchyActorMixer mixer => mixer.BaseParams,
            _ => null,
        } : null;
    }

    private WwiseMix MixOf(BaseHierarchy baseParams) => MixOf(baseParams.PropBundle);

    private WwiseMix MixOf(AkPropBundle properties)
    {
        float volume = 0f, pitch = 0f, volumeMin = 0f, volumeMax = 0f, pitchMin = 0f, pitchMax = 0f;
        float? attenuation = null;
        var loops = false;
        foreach (var property in properties.Props)
        {
            switch (property.Id)
            {
                case _volumeProperty:
                    volume = property.Value.f32;
                    break;
                case _pitchProperty:
                    pitch = property.Value.f32;
                    break;
                case _loopProperty:
                    // The loop count; zero repeats forever, and a set number of repeats has no Source equivalent.
                    loops = property.Value.u32 == 0;
                    break;
                case _attenuationProperty:
                    attenuation = AttenuationMeters(property.Value.u32);
                    break;
            }
        }

        foreach (var range in properties.PropRanges)
        {
            switch (range.Id)
            {
                case _volumeProperty:
                    (volumeMin, volumeMax) = (range.Min.f32, range.Max.f32);
                    break;
                case _pitchProperty:
                    (pitchMin, pitchMax) = (range.Min.f32, range.Max.f32);
                    break;
            }
        }

        return new WwiseMix(volume, volumeMin, volumeMax, pitch, pitchMin, pitchMax, attenuation, loops);
    }

    // Where the attenuation's volume curve, its first, reaches its end: the distance past which
    // the sound is no longer heard.
    private float? AttenuationMeters(uint attenuationId)
    {
        return _hierarchies.TryGetValue(attenuationId, out var hierarchy) &&
            hierarchy.Data is HierarchyAttenuation { Curves: [{ GraphPoints: [.., var last] }, ..] } &&
            last.From > 0f
                ? last.From / _unitsPerMeter
                : null;
    }

    private static WwiseReader? TryReadBankFile(DefaultFileProvider provider, string bankName)
    {
        var suffix = $"/{bankName}.bnk";
        var file = provider.Files.Values.FirstOrDefault(candidate => candidate.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (file is null || !file.TryRead(out var data))
        {
            return null;
        }

        try
        {
            return new WwiseReader(new FWwiseArchive(file.NameWithoutExtension, data), new WwiseGameFileSource(file));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            PerfTimer.Log("ue4.wwise", $"soundbank {bankName} unreadable: {ex.Message}");
            return null;
        }
    }
}
