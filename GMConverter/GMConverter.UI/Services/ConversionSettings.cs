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
    OptionValues ExporterOptions);
