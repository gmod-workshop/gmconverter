using GMConverter.Godot.Audio;
using GMConverter.Godot.Resources;
using GMConverter.SDK.Audio;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;

namespace GMConverter.Godot.Importers;

/// <summary>
/// Reads a sound Godot's importer wrote, as the explorer extracts it from a game's pack: an Ogg
/// Vorbis stream (<c>.oggvorbisstr</c>) or a WAV sample (<c>.sample</c>). Each becomes one clip.
/// </summary>
internal sealed class GodotSoundImporter : ISoundImporter
{
    public string Format => "godotaudio";

    public string DisplayName => "Godot audio stream";

    public IReadOnlyList<string> FileExtensions { get; } = [GodotAudio.OggExtension, GodotAudio.SampleExtension];

    public OptionSchema OptionSchema => OptionSchema.Empty;

    public Sound Import(SoundImportContext context)
    {
        var name = Path.GetFileNameWithoutExtension(context.InputPath);
        var stream = GodotResourceFile.Read(File.ReadAllBytes(context.InputPath)).Main;
        return new Sound(name, [GodotAudio.Decode(stream, name)], []);
    }

    public object Summarize(string inputPath)
    {
        var stream = GodotResourceFile.Read(File.ReadAllBytes(inputPath)).Main;
        return new { Name = Path.GetFileNameWithoutExtension(inputPath), stream.Type };
    }
}
