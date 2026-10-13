using GMConverter.SDK.Explorer;
using GMConverter.SDK.Options;
using GMConverter.SDK.Profiles;
using GMConverter.SourceEngine.Audio;

namespace GMConverter.SourceEngine.Profiles;

/// <summary>Any other Source game: sound definitions go in a sound script, which the game loads once listed in its sound manifest.</summary>
internal sealed class GenericSourceSoundProfile : ISourceSoundProfile
{
    private static readonly GenericSourceProfile _game = new();

    public string Id => _game.Id;

    public string DisplayName => _game.DisplayName;

    public string Engine => _game.Engine;

    public string ExporterFormat => "wav";

    public OptionSchema OptionSchema => OptionSchema.Empty;

    public GameMatch Detect(ExplorerTarget target) => _game.Detect(target);

    public (string RelativePath, string Contents) WriteDefinitions(IReadOnlyList<SourceSoundDefinition> definitions, string name)
    {
        return (Path.Join("scripts", $"{name}_sounds.txt"), SoundScriptWriter.Write(definitions));
    }
}
