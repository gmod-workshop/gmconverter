using GMConverter.SDK.Options;

namespace GMConverter.SDK.Importers;

/// <summary>
/// Settings for one <see cref="IImporter.Parse"/> call. The positional members are host-owned
/// settings every importer honours; <see cref="Options"/> carries the values for the importer's
/// own <see cref="IImporter.OptionSchema"/>.
/// </summary>
public sealed record ModelParseOptions(
    float ScaleFactor,
    ModelAxisMode AxisMode = ModelAxisMode.Auto,
    MaterialResolveOptions? Materials = null)
{
    /// <summary>Values for the importer's own option schema, keyed by option key.</summary>
    public OptionValues Options { get; init; } = OptionValues.Empty;
}
