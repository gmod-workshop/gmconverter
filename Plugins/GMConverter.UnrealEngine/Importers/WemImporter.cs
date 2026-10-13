using GMConverter.SDK.Audio;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;
using GMConverter.UnrealEngine.Audio;

namespace GMConverter.UnrealEngine.Importers;

/// <summary>Reads a Wwise <c>.wem</c> media file, decoded with vgmstream, as a sound with one clip and no events.</summary>
internal sealed class WemImporter : ISoundImporter
{
    public string Format => "wem";

    public string DisplayName => "Wwise media";

    public IReadOnlyList<string> FileExtensions { get; } = [".wem"];

    public OptionSchema OptionSchema { get; } = new([new OptionGroup("tools", "Tools", [WwiseEventImporter.VgmstreamPathDescriptor()])]);

    public Sound Import(SoundImportContext context)
    {
        var name = Path.GetFileNameWithoutExtension(context.InputPath);
        var clip = VgmstreamDecoder.Resolve(context.Options.GetString(WwiseEventImporter.VgmstreamPathOption)).Decode(context.InputPath, name);
        return new Sound(name, [clip], []);
    }

    public object Summarize(string inputPath)
    {
        var info = new FileInfo(inputPath);
        return new { Name = Path.GetFileNameWithoutExtension(inputPath), Bytes = info.Length };
    }
}
