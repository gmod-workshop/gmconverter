using GMConverter.SDK.Animation;
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
    /// <summary>The game the input comes from, or null when unknown.</summary>
    public string? SourceGameId { get; init; }

    /// <summary>Roles chosen for animations by clip name, replacing the guessed ones.</summary>
    public IReadOnlyDictionary<string, AnimationRole?> RoleOverrides { get; init; } = new Dictionary<string, AnimationRole?>();

    /// <summary>The game to export for, or null for the exporter's default target.</summary>
    public string? TargetGameId { get; init; }

    /// <summary>Values for the target game's own options.</summary>
    public OptionValues TargetOptions { get; init; } = OptionValues.Empty;
}
