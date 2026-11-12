using GMConverter.SDK.Explorer;
using GMConverter.SDK.Options;
using GMConverter.SDK.Profiles;

namespace GMConverter.SourceEngine.Profiles;

/// <summary>Any other Source game: particle files load once listed in the game's <c>particles/particles_manifest.txt</c>.</summary>
internal sealed class GenericSourceParticleProfile : ISourceParticleProfile
{
    private static readonly GenericSourceProfile _game = new();

    public string Id => _game.Id;

    public string DisplayName => _game.DisplayName;

    public string Engine => _game.Engine;

    public string ExporterFormat => "pcf";

    public OptionSchema OptionSchema => OptionSchema.Empty;

    public GameMatch Detect(ExplorerTarget target) => _game.Detect(target);

    public (string? RelativePath, string Contents) WriteLoader(string pcfPath, IReadOnlyList<string> systems, string name)
    {
        return (null, $"add \"file\" \"{pcfPath.Replace(Path.DirectorySeparatorChar, '/')}\" to particles/particles_manifest.txt to load it.");
    }
}
