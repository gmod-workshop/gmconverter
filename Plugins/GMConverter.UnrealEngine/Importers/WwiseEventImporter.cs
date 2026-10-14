using GMConverter.SDK.Audio;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;
using GMConverter.UnrealEngine.Formats.Wwise;

namespace GMConverter.UnrealEngine.Importers;

/// <summary>
/// Reads a Wwise event the Unreal explorer exported (a <c>.akevent</c> sidecar beside its
/// <c>.wem</c> media) as a sound with one event that picks between the media at random.
/// </summary>
internal sealed class WwiseEventImporter : ISoundImporter
{
    public const string FormatId = "akevent";

    public string Format => FormatId;

    public string DisplayName => "Wwise event (Unreal Explorer)";

    public IReadOnlyList<string> FileExtensions { get; } = [WwiseEventSidecar.Extension];

    public OptionSchema OptionSchema { get; } = new([new OptionGroup("tools", "Tools", [VgmstreamDecoder.PathOptionDescriptor("Wwise media")])]);

    public Sound Import(SoundImportContext context)
    {
        var sidecar = WwiseEventSidecar.Read(context.InputPath);
        var directory = Path.GetDirectoryName(context.InputPath)!;
        var decoder = VgmstreamDecoder.Resolve(context.Options.GetString(VgmstreamDecoder.PathOption));

        // Each decode is its own vgmstream process, and footstep events can hold hundreds of
        // variations, so they run side by side.
        var clips = new SoundClip[sidecar.Media.Count];
        Parallel.For(0, clips.Length, index =>
        {
            var file = sidecar.Media[index];
            clips[index] = decoder.Decode(Path.Join(directory, file), Path.GetFileNameWithoutExtension(file));
        });

        var soundEvent = new SoundEvent(sidecar.Name, [.. clips.Select(clip => new SoundEventEntry(clip.Name))])
        {
            AttenuationMeters = sidecar.AttenuationMeters,
            Category = sidecar.Category,
            Loops = clips.Any(clip => clip.Loop is not null),
        };
        return new Sound(sidecar.Name, clips, [soundEvent]);
    }

    public object Summarize(string inputPath)
    {
        var sidecar = WwiseEventSidecar.Read(inputPath);
        return new
        {
            sidecar.Name,
            Category = sidecar.Category.ToString(),
            sidecar.AttenuationMeters,
            MediaCount = sidecar.Media.Count,
            sidecar.Media,
        };
    }
}
