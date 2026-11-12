using GMConverter.SDK.Audio;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;
using GMConverter.UnrealEngine.Formats.UE3.Audio;

namespace GMConverter.UnrealEngine.Importers;

/// <summary>
/// Reads a UE3 SoundCue the explorer exported (<see cref="UE3SoundCueSidecar"/>): one sound event per
/// layer, named as animation events play it, with its audio decoded by vgmstream.
/// </summary>
internal sealed class UE3SoundCueImporter : ISoundImporter
{
    public const string FormatId = "ue3cue";
    public const string VgmstreamPathOption = VgmstreamDecoder.PathOption;

    public string Format => FormatId;

    public string DisplayName => "Unreal Engine 3 sound cue (Unreal Explorer)";

    public IReadOnlyList<string> FileExtensions { get; } = [UE3SoundCueSidecar.Extension];

    public OptionSchema OptionSchema { get; } = new([new OptionGroup("tools", "Tools", [VgmstreamDecoder.PathOptionDescriptor("UE3 sound cues")])]);

    public Sound Import(SoundImportContext context)
    {
        var sidecar = UE3SoundCueSidecar.Read(context.InputPath);
        var directory = Path.GetDirectoryName(context.InputPath)!;
        var decoder = VgmstreamDecoder.Resolve(context.Options.GetString(VgmstreamPathOption));
        var files = sidecar.Layers.SelectMany(layer => layer.Files).Select(file => file.File).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var clips = new SoundClip[files.Length];
        Parallel.For(0, clips.Length, index => clips[index] = decoder.Decode(Path.Join(directory, files[index]), Path.GetFileNameWithoutExtension(files[index])));

        var events = sidecar.Layers.Select((layer, index) => new SoundEvent(
            UE3SoundCue.LayerName(sidecar.Name, index),
            [.. layer.Files.Select(file => new SoundEventEntry(Path.GetFileNameWithoutExtension(file.File), file.Weight))])
        {
            Volume = new SoundRange(layer.VolumeMin, layer.VolumeMax),
            Pitch = new SoundRange(layer.PitchMin, layer.PitchMax),
            AttenuationMeters = layer.AttenuationMeters,
            Category = sidecar.Category,
            Loops = layer.Loops,
        }).ToArray();
        return new Sound(sidecar.Name, clips, events);
    }

    public object Summarize(string inputPath)
    {
        var sidecar = UE3SoundCueSidecar.Read(inputPath);
        return new
        {
            sidecar.Name,
            Category = sidecar.Category.ToString(),
            Layers = sidecar.Layers.Count,
            Files = sidecar.Layers.Sum(layer => layer.Files.Count),
        };
    }
}
