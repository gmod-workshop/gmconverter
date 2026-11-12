using GMConverter.SDK.Audio;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;
using GMConverter.Unity.Audio;

namespace GMConverter.Unity.Importers;

/// <summary>
/// Reads an FMOD sound bank (<c>.fsb</c>), decoded with vgmstream, as a sound with a clip for each
/// sound in it and no events. Unity audio clips resolve to one-sound banks.
/// </summary>
internal sealed class FsbImporter : ISoundImporter
{
    public string Format => "fsb";

    public string DisplayName => "FMOD sound bank";

    public IReadOnlyList<string> FileExtensions { get; } = [UnityAudioClip.BankExtension];

    public OptionSchema OptionSchema { get; } = new([new OptionGroup("tools", "Tools", [VgmstreamDecoder.PathOptionDescriptor("FMOD sound banks")])]);

    public Sound Import(SoundImportContext context)
    {
        var name = Path.GetFileNameWithoutExtension(context.InputPath);
        var clips = VgmstreamDecoder.Resolve(context.Options.GetString(VgmstreamDecoder.PathOption)).DecodeAll(context.InputPath, name);
        return new Sound(name, clips, []);
    }

    public object Summarize(string inputPath)
    {
        var info = new FileInfo(inputPath);
        return new { Name = Path.GetFileNameWithoutExtension(inputPath), Bytes = info.Length };
    }
}
