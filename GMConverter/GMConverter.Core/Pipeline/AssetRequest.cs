using GMConverter.Core.Formats;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;

namespace GMConverter.Core.Pipeline;

/// <summary>
/// One asset to convert: where it comes from, which importer reads it, and which output format
/// it is written to. <see cref="OutputFormat"/> and <see cref="OutputDirectory"/> may be left
/// unset when the asset is only imported (preview, summary); running the request through
/// <see cref="AssetJobRunner"/> requires both.
/// </summary>
/// <param name="InputFormat">The importer's input format, e.g. <c>"psk"</c>.</param>
/// <param name="InputPath">The file to import.</param>
internal sealed record AssetRequest(string InputFormat, string InputPath)
{
    /// <summary>An output format from <see cref="FormatCatalog.Exports"/>, aliases included.</summary>
    public string? OutputFormat { get; init; }

    public string? OutputDirectory { get; init; }

    /// <summary>Base name for written files; defaults to the input file's name.</summary>
    public string? BaseName { get; init; }

    public OptionValues ImporterOptions { get; init; } = OptionValues.Empty;

    public OptionValues ExporterOptions { get; init; } = OptionValues.Empty;

    public float ScaleFactor { get; init; } = 1.0f;

    public ModelAxisMode AxisMode { get; init; } = ModelAxisMode.Auto;

    /// <summary>Optional directory searched for sidecar materials and textures.</summary>
    public string? MaterialDirectory { get; init; }

    /// <summary>
    /// The game the input comes from, or null when unknown. Its profile fills in what the
    /// game's conventions reveal (such as animation roles) after import.
    /// </summary>
    public string? SourceGameId { get; init; }

    /// <summary>The game to export for, or null for the exporter's default target.</summary>
    public string? TargetGameId { get; init; }

    /// <summary>Values for the target game's own options.</summary>
    public OptionValues TargetOptions { get; init; } = OptionValues.Empty;
}
