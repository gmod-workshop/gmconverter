using GMConverter.SDK.Audio;
using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Formats.UE3.Materials;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Audio;

/// <summary>
/// A UE3 SoundCue flattened from its node graph into layers that a sound script can play: random
/// nodes become weighted choices, modulators pitch and volume ranges, attenuation a hearing distance,
/// delays a start offset, and each branch of a mixer its own layer.
/// </summary>
internal sealed record UE3SoundCue(string Name, SoundCategory Category, IReadOnlyList<UE3SoundLayer> Layers)
{
    // UE3's class defaults, which cooked packages leave out: SoundCue.VolumeMultiplier and the
    // DistributionFloatUniform ranges of SoundNodeModulator's volume and pitch.
    private const float _defaultCueVolume = 0.75f;
    private const float _defaultModulation = 0.05f;
    private const int _maximumDepth = 32;

    /// <summary>The sound event name for a cue's layer: the cue's own name, then numbered for further layers.</summary>
    public static string LayerName(string cueName, int layer) => layer == 0 ? cueName : FormattableString.Invariant($"{cueName}_{layer + 1}");

    public static UE3SoundCue Read(UE3GameContent content, UE3Package package, UE3PackageExport export)
    {
        var properties = package.OpenObject(export).Properties;
        var volume = properties.GetFloat("VolumeMultiplier") ?? _defaultCueVolume;
        var pitch = properties.GetFloat("PitchMultiplier") ?? 1f;
        var start = new Branch([], SoundRange.Fixed(volume), SoundRange.Fixed(pitch), 0f, null, false);
        var layers = Walk(content, package, properties.GetObject("FirstNode"), start, 0)
            .Where(branch => branch.Waves.Count > 0)
            .Select(branch => new UE3SoundLayer(branch.Waves, branch.Volume, branch.Pitch, branch.Delay, branch.Attenuation, branch.Loops))
            .ToArray();
        var soundClass = properties.GetName("SoundClass") ?? string.Empty;
        return new UE3SoundCue(export.ObjectName, Categorise(soundClass), layers);
    }

    private static List<Branch> Walk(UE3GameContent content, UE3Package package, int nodeIndex, Branch state, int depth)
    {
        if (nodeIndex == 0 || depth > _maximumDepth || content.Resolve(package, nodeIndex) is not { } node)
        {
            return [];
        }

        var (nodePackage, nodeExport) = node;
        UE3Properties properties;
        try
        {
            properties = nodePackage.OpenObject(nodeExport).Properties;
        }
        catch (GMConverterException)
        {
            return [];
        }

        var children = properties.GetObjectArray("ChildNodes");
        List<Branch> Child(int index, Branch with) => index < children.Count ? Walk(content, nodePackage, children[index], with, depth + 1) : [];
        List<Branch> AllChildren(Branch with) => [.. children.SelectMany((_, index) => Child(index, with))];

        switch (nodePackage.GetClassName(nodeExport))
        {
            case "SoundNodeWave":
                return [state with { Waves = [(new UE3ObjectReference(nodePackage, nodeExport), 1f)] }];
            case "SoundNodeAttenuation":
                var radius = properties.GetFloat("RadiusMax");
                return AllChildren(state with { Attenuation = radius is { } units ? units / 100f : state.Attenuation });
            case "SoundNodeModulator":
                return AllChildren(state with
                {
                    Volume = Scale(state.Volume, 1 - _defaultModulation, 1 + _defaultModulation),
                    Pitch = Scale(state.Pitch, 1 - _defaultModulation, 1 + _defaultModulation),
                });
            case "SoundNodeDelay":
                var delay = ((properties.GetFloat("DelayMin") ?? 0f) + (properties.GetFloat("DelayMax") ?? 0f)) / 2f;
                return AllChildren(state with { Delay = state.Delay + delay });
            case "SoundNodeLooping":
                return AllChildren(state with { Loops = true });
            case "SoundNodeMixer":
                var inputs = ReadFloats(properties, "InputVolume");
                return [.. children.SelectMany((_, index) => Child(index, state with
                {
                    Volume = Scale(state.Volume, index < inputs.Count ? inputs[index] : 1f, index < inputs.Count ? inputs[index] : 1f),
                }))];
            case "SoundNodeRandom":
                return Random(children, ReadFloats(properties, "Weights"), Child, state);
            default:
                // Nodes whose effect a sound script can't express (concatenators, distance crossfades)
                // play their first input.
                return Child(0, state);
        }
    }

    // A random node picks one input per play. When each input is a single choice of waves, they merge
    // into one weighted choice; inputs that layer several parts keep the first input's layers.
    private static List<Branch> Random(IReadOnlyList<int> children, IReadOnlyList<float> weights, Func<int, Branch, List<Branch>> child, Branch state)
    {
        var inputs = children.Select((_, index) => child(index, state)).ToArray();
        if (inputs.Length == 0)
        {
            return [];
        }

        if (inputs.Any(input => input.Count != 1))
        {
            return inputs.FirstOrDefault(input => input.Count > 0) ?? [];
        }

        var total = inputs.Select((input, index) => index < weights.Count ? weights[index] : 1f).Sum();
        List<(UE3ObjectReference Wave, float Weight)> waves = [];
        for (var index = 0; index < inputs.Length; index++)
        {
            var weight = index < weights.Count ? weights[index] : 1f;
            var branchTotal = inputs[index][0].Waves.Sum(wave => wave.Weight);
            waves.AddRange(inputs[index][0].Waves.Select(wave => (wave.Wave, wave.Weight / branchTotal * weight / total)));
        }

        return [inputs[0][0] with { Waves = waves }];
    }

    private static IReadOnlyList<float> ReadFloats(UE3Properties properties, string name)
    {
        if (properties.GetArray(name) is not { } array)
        {
            return [];
        }

        return [.. Enumerable.Range(0, array.Count).Select(_ => array.Elements.ReadSingle())];
    }

    private static SoundRange Scale(SoundRange range, float min, float max) => new(range.Min * min, range.Max * max);

    private static SoundCategory Categorise(string soundClass)
    {
        bool Has(string word) => soundClass.Contains(word, StringComparison.OrdinalIgnoreCase);
        return true switch
        {
            _ when Has("Weapon") => SoundCategory.Weapon,
            _ when Has("Voice") || Has("Dialog") => SoundCategory.Voice,
            _ when Has("Foot") => SoundCategory.Footstep,
            _ when Has("Music") => SoundCategory.Music,
            _ when Has("UI") || Has("Interface") => SoundCategory.UI,
            _ when Has("Ambient") => SoundCategory.Ambient,
            _ when Has("Impact") || Has("Hit") => SoundCategory.Impact,
            _ => SoundCategory.Other,
        };
    }

    private sealed record Branch(
        List<(UE3ObjectReference Wave, float Weight)> Waves,
        SoundRange Volume,
        SoundRange Pitch,
        float Delay,
        float? Attenuation,
        bool Loops);
}
