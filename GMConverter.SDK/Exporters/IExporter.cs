using GMConverter.SDK.Geometry;

namespace GMConverter.SDK.Exporters;

/// <summary>
/// Exports a <see cref="Model"/> to a target format.
/// </summary>
/// <typeparam name="TOptions"></typeparam>
public interface IExporter<in TOptions> : IExporterDescriptor
{
    void Export(Model model, string outputDirectory, string baseName, TOptions options);
}
