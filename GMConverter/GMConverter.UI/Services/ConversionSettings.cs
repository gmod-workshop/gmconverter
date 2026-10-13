using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;

namespace GMConverter.UI.Services;

internal sealed record ConversionSettings(
    string InputFormat,
    string OutputFormat,
    string InputPath,
    string? OutputPath,
    string? BaseName,
    string? MaterialDirectory,
    float ScaleFactor,
    ModelAxisMode AxisMode,
    OptionValues ImporterOptions,
    OptionValues ExporterOptions)
{
    /// <summary>The game to export for, or null for the exporter's default target.</summary>
    public string? TargetGameId { get; init; }

    /// <summary>Values for the target game's own options.</summary>
    public OptionValues TargetOptions { get; init; } = OptionValues.Empty;
}
