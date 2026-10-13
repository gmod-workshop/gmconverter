using GMConverter.SDK.Audio;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;
using GMConverter.UnrealEngine.Audio;
using GMConverter.UnrealEngine.Formats.Wwise;

namespace GMConverter.UnrealEngine.Importers;

/// <summary>
/// Reads a Wwise event the Unreal explorer exported (a <c>.akevent</c> sidecar beside its
/// <c>.wem</c> media) as a sound with one event, mixed as the sidecar says.
/// </summary>
internal sealed class WwiseEventImporter : ISoundImporter
{
    public const string FormatId = "akevent";
    public const string VgmstreamPathOption = "vgmstreamPath";

    public string Format => FormatId;

    public string DisplayName => "Wwise event (Unreal Explorer)";

    public IReadOnlyList<string> FileExtensions { get; } = [WwiseEventSidecar.Extension];

    public OptionSchema OptionSchema { get; } = new([new OptionGroup("tools", "Tools", [VgmstreamPathDescriptor()])]);

    internal static OptionDescriptor VgmstreamPathDescriptor() => new(VgmstreamPathOption, OptionType.Path, "vgmstream override")
    {
        Description = "Optional vgmstream-cli executable for decoding Wwise media. Auto-downloads to tools/ when omitted.",
        DefaultValueFactory = VgmstreamDecoder.TryFindLocal,
    };

    public Sound Import(SoundImportContext context)
    {
        var sidecar = WwiseEventSidecar.Read(context.InputPath);
        var directory = Path.GetDirectoryName(context.InputPath)!;
        var decoder = VgmstreamDecoder.Resolve(context.Options.GetString(VgmstreamPathOption));

        // Each decode is its own vgmstream process, and footstep events can hold dozens of
        // variations, so they run side by side.
        var clips = new SoundClip[sidecar.Media.Count];
        Parallel.For(0, clips.Length, index =>
        {
            var media = sidecar.Media[index];
            var clip = decoder.Decode(Path.Join(directory, media.File), Path.GetFileNameWithoutExtension(media.File));
            clips[index] = WithGain(clip, media.GainDb);
        });

        var entries = clips.Select((clip, index) => new SoundEventEntry(clip.Name, sidecar.Media[index].Weight));
        var soundEvent = new SoundEvent(sidecar.Name, [.. entries])
        {
            Selection = sidecar.Selection,
            Volume = new SoundRange(Gain(sidecar.VolumeDb + sidecar.VolumeMinDb), Gain(sidecar.VolumeDb + sidecar.VolumeMaxDb)),
            Pitch = new SoundRange(PitchRatio(sidecar.PitchCents + sidecar.PitchMinCents), PitchRatio(sidecar.PitchCents + sidecar.PitchMaxCents)),
            AttenuationMeters = sidecar.AttenuationMeters,
            Category = sidecar.Category,
            Loops = sidecar.Loops || clips.Any(clip => clip.Loop is not null),
        };
        return new Sound(sidecar.Name, clips, [soundEvent]);
    }

    // Decibels to a linear gain: -6 dB is about half.
    internal static float Gain(float decibels) => MathF.Pow(10f, decibels / 20f);

    // Cents to a playback rate: 1200 cents is an octave, twice the rate.
    internal static float PitchRatio(float cents) => MathF.Pow(2f, cents / 1200f);

    // A media file quieter than the event's loudest is made quieter in its samples, since the
    // event has one volume for all of them. Gains are never above zero decibels, so nothing clips.
    private static SoundClip WithGain(SoundClip clip, float gainDb)
    {
        if (gainDb >= 0f)
        {
            return clip;
        }

        var gain = Gain(gainDb);
        return clip with { Samples = [.. clip.Samples.Select(sample => sample * gain)] };
    }

    public object Summarize(string inputPath)
    {
        var sidecar = WwiseEventSidecar.Read(inputPath);
        return new
        {
            sidecar.Name,
            Category = sidecar.Category.ToString(),
            sidecar.AttenuationMeters,
            sidecar.VolumeDb,
            sidecar.PitchMinCents,
            sidecar.PitchMaxCents,
            Selection = sidecar.Selection.ToString(),
            MediaCount = sidecar.Media.Count,
            Media = sidecar.Media.Select(media => media.File).ToList(),
        };
    }
}
