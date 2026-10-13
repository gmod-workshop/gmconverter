namespace GMConverter.SDK.Importers;

/// <summary>
/// What a model importer is asked to read. The positional members are host-owned settings every
/// model importer honours; <see cref="ImportContext.Options"/> carries the importer's own options.
/// </summary>
/// <param name="InputPath">Full path of the file to read.</param>
/// <param name="ScaleFactor">Scale applied to the imported geometry.</param>
/// <param name="AxisMode">The input's up axis, or <see cref="ModelAxisMode.Auto"/> to detect it.</param>
/// <param name="Materials">Where to look for sidecar materials and textures, if anywhere.</param>
public sealed record ModelImportContext(
    string InputPath,
    float ScaleFactor = 1.0f,
    ModelAxisMode AxisMode = ModelAxisMode.Auto,
    MaterialResolveOptions? Materials = null)
    : ImportContext(InputPath);
