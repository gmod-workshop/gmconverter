using GMConverter.SDK.Explorer;
using GMConverter.SDK.Games;
using GMConverter.SDK.Options;

namespace GMConverter.SourceEngine.Games;

/// <summary>
/// Any other Source game: plain MDL output with nothing game-specific added, as the exporter
/// wrote before targets existed.
/// </summary>
internal sealed class SourceGameProfile : IExportGameProfile
{
    public string Id => "source";

    public string DisplayName => "Other Source game";

    public string Engine => "Source";

    public string ExporterFormat => "mdl";

    public OptionSchema OptionSchema => OptionSchema.Empty;

    /// <summary>A fallback for any folder holding a Source <c>gameinfo.txt</c>.</summary>
    public GameMatch Detect(ExplorerTarget target)
    {
        return target.IsDirectory && File.Exists(Path.Join(target.FullPath, "gameinfo.txt")) ? GameMatch.Fallback : GameMatch.None;
    }
}
