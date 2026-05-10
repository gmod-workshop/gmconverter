using GMConverter.Geometry;

namespace GMConverter.Exporters;

/// <summary>
/// Exports a <see cref="Model"/> to a target format.
/// </summary>
/// <typeparam name="TOptions"></typeparam>
internal interface IExporter<in TOptions>
{
    void Export(Model model, string outputDirectory, string baseName, TOptions options);
}
