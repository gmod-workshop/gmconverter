using GMConverter.SDK.Explorer;
using GMConverter.SDK.Options;
using GMConverter.SDK.Profiles;
using GMConverter.SourceEngine.Audio;

namespace GMConverter.SourceEngine.Profiles.GarrysMod;

/// <summary>
/// Garry's Mod, the sound exporter's default target. It does not load sound scripts from add-ons,
/// so sound definitions become <c>sound.Add</c> calls in a Lua file under <c>lua/autorun</c>, which
/// runs on both client and server.
/// </summary>
internal sealed class GarrysModSoundProfile : ISourceSoundProfile
{
    private static readonly GarrysModProfile _game = new();

    public string Id => _game.Id;

    public string DisplayName => _game.DisplayName;

    public string Engine => _game.Engine;

    public string ExporterFormat => "wav";

    public OptionSchema OptionSchema => OptionSchema.Empty;

    public GameMatch Detect(ExplorerTarget target) => _game.Detect(target);

    public (string RelativePath, string Contents) WriteDefinitions(IReadOnlyList<SourceSoundDefinition> definitions, string name)
    {
        return (Path.Join("lua", "autorun", $"{name}_sounds.lua"), SoundLuaWriter.Write(definitions));
    }
}
