using System.Text;
using GMConverter.SDK.Audio;
using GMConverter.SDK.Common;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Options;
using GMConverter.SourceEngine.Audio;
using GMConverter.SourceEngine.Profiles;

namespace GMConverter.SourceEngine.Exporters;

/// <summary>
/// Writes a sound for Source: each clip as a WAV under <c>sound/</c> in a format Source plays, and
/// each event as a sound definition in the form the target game loads (see <see cref="ISourceSoundProfile"/>).
/// </summary>
internal sealed class WAVExporter : ISoundExporter
{
    public const string SoundPathOption = "soundPath";
    public const string SampleRateOption = "sampleRate";
    public const string ChannelsOption = "channels";

    private const string _auto = "auto";
    private const string _mono = "mono";
    private const string _keep = "keep";

    // The only rates Source's mixer plays without artefacts.
    private static readonly int[] _sourceSampleRates = [11025, 22050, 44100];
    private static readonly UTF8Encoding _utf8NoBom = new(false);

    public string Format => "wav";

    public string DisplayName => "Source Engine sounds";

    public OptionSchema OptionSchema { get; } = new(
    [
        new OptionGroup("sound", "Sound",
        [
            new OptionDescriptor(SoundPathOption, OptionType.String, "Sound path")
            {
                Description = "Folder for the WAVs under the game's sound directory. Defaults to gmconverter/<name>.",
            },
            new OptionDescriptor(SampleRateOption, OptionType.Enum, "Sample rate")
            {
                DefaultValue = _auto,
                Choices = [_auto, "44100", "22050", "11025"],
                Description = "auto keeps a rate Source plays (44100, 22050 or 11025 Hz) and otherwise resamples to the nearest one above, up to 44100.",
            },
            new OptionDescriptor(ChannelsOption, OptionType.Enum, "Channels")
            {
                DefaultValue = _auto,
                Choices = [_auto, _mono, _keep],
                Description = "auto makes sounds heard from a place in the world mono, since Source only positions mono sounds, and keeps the rest as recorded.",
            },
        ]),
    ]);

    public ExportResult Export(Sound sound, ExportContext context)
    {
        var name = NameHelpers.SanitizeMaterialName(context.BaseName);
        var soundPath = SoundPath(context.Options.GetString(SoundPathOption), name);
        var soundDirectory = Path.Join(context.OutputDirectory, "sound", soundPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(soundDirectory);

        var positional = ClipsWhere(sound, soundEvent => soundEvent.AttenuationMeters is not null);
        var looping = ClipsWhere(sound, soundEvent => soundEvent.Loops);
        var channels = context.Options.GetString(ChannelsOption) ?? _auto;
        var sampleRate = context.Options.GetString(SampleRateOption) ?? _auto;

        List<string> waveFiles = [];
        Dictionary<string, string> wavePaths = new(StringComparer.Ordinal);
        HashSet<string> fileNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in sound.Clips)
        {
            var fileName = UniqueFileName(clip.Name, fileNames);
            var prepared = Prepare(clip, sampleRate, channels == _mono || (channels == _auto && positional.Contains(clip.Name)));
            if (prepared.Loop is null && looping.Contains(clip.Name))
            {
                prepared = prepared with { Loop = new SoundLoop() };
            }

            var path = Path.Join(soundDirectory, fileName);
            WaveFile.Write(path, prepared);
            waveFiles.Add(path);
            wavePaths[clip.Name] = $"{soundPath}/{fileName}";
        }

        var definitions = SourceSoundDefinitions.Create(sound, wavePaths);
        if (definitions.Count == 0)
        {
            return new ExportResult(waveFiles);
        }

        // Without a chosen game, a sound script is the form every Source game can load.
        var profile = SourceSoundProfiles.Find(context.TargetGameId) ?? SourceSoundProfiles.Find("source")!;
        var (relativePath, contents) = profile.WriteDefinitions(definitions, name);
        var definitionsPath = Path.Join(context.OutputDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(definitionsPath)!);
        File.WriteAllText(definitionsPath, contents, _utf8NoBom);
        return new ExportResult([definitionsPath, .. waveFiles]);
    }

    internal static SoundClip Prepare(SoundClip clip, string sampleRate, bool mono)
    {
        // Source plays mono and stereo only.
        if (mono || clip.Channels > 2)
        {
            clip = SoundResampler.ToMono(clip);
        }

        return SoundResampler.Resample(clip, TargetSampleRate(clip.SampleRate, sampleRate));
    }

    internal static int TargetSampleRate(int sampleRate, string choice)
    {
        if (int.TryParse(choice, out var chosen))
        {
            return chosen;
        }

        return _sourceSampleRates.Contains(sampleRate)
            ? sampleRate
            : _sourceSampleRates.FirstOrDefault(rate => rate >= sampleRate, _sourceSampleRates[^1]);
    }

    private static string SoundPath(string? option, string name)
    {
        var path = string.IsNullOrWhiteSpace(option) ? $"gmconverter/{name}" : option;
        return path.Replace('\\', '/').Trim('/').ToLowerInvariant();
    }

    private static HashSet<string> ClipsWhere(Sound sound, Func<SoundEvent, bool> predicate)
    {
        return [.. sound.Events.Where(predicate).SelectMany(soundEvent => soundEvent.Entries).Select(entry => entry.Clip)];
    }

    // Source looks sounds up by lowercase path, so clips whose names differ only by case or by
    // characters a path cannot hold get numbered instead of overwriting each other.
    private static string UniqueFileName(string clipName, HashSet<string> used)
    {
        var stem = NameHelpers.SanitizeMaterialName(clipName);
        var fileName = $"{stem}.wav";
        for (var index = 2; !used.Add(fileName); index++)
        {
            fileName = $"{stem}_{index}.wav";
        }

        return fileName;
    }
}
