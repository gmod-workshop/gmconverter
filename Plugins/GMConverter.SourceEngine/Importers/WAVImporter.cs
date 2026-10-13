using GMConverter.SDK.Audio;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;

namespace GMConverter.SourceEngine.Importers;

/// <summary>Reads a WAV file as a sound with one clip and no events.</summary>
internal sealed class WAVImporter : ISoundImporter
{
    public string Format => "wav";

    public string DisplayName => "WAV audio";

    public IReadOnlyList<string> FileExtensions { get; } = [".wav"];

    public OptionSchema OptionSchema => OptionSchema.Empty;

    public Sound Import(SoundImportContext context)
    {
        var clip = WaveFile.Read(context.InputPath);
        return new Sound(clip.Name, [clip], []);
    }

    public object Summarize(string inputPath)
    {
        var clip = WaveFile.Read(inputPath);
        return new
        {
            clip.Name,
            clip.SampleRate,
            clip.Channels,
            DurationSeconds = Math.Round(clip.DurationSeconds, 3),
            Loops = clip.Loop is not null,
        };
    }
}
