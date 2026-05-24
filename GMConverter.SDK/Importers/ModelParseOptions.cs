namespace GMConverter.SDK.Importers;

public sealed record ModelParseOptions(
    float ScaleFactor,
    ModelAxisMode AxisMode = ModelAxisMode.Auto,
    MaterialResolveOptions? Materials = null,
    string? AnimationPath = null);
