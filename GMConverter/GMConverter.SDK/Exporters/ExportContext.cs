using GMConverter.SDK.Options;

namespace GMConverter.SDK.Exporters;

/// <summary>Where and how an exporter writes its asset.</summary>
/// <param name="OutputDirectory">Full path of the directory to write into. The host creates it.</param>
/// <param name="BaseName">Base name for the written files, before extensions.</param>
/// <param name="Options">Values for the exporter's own <see cref="IExporter.OptionSchema"/>, with defaults filled in.</param>
public sealed record ExportContext(string OutputDirectory, string BaseName, OptionValues Options)
{
    /// <summary>
    /// The <see cref="Games.IGameProfile.Id"/> of the game being exported for, or null when the
    /// exporter has no targets. Plugins look up their own settings for the game by this id.
    /// </summary>
    public string? TargetGameId { get; init; }

    /// <summary>Values for the target game's <see cref="Games.IExportGameProfile.OptionSchema"/>, with defaults filled in.</summary>
    public OptionValues TargetOptions { get; init; } = OptionValues.Empty;
}
