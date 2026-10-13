using GMConverter.SDK.Assets;
using GMConverter.SDK.Geometry;

namespace GMConverter.SDK.Exporters;

/// <summary>An exporter that writes a <see cref="Model"/>. Its <see cref="IExporter.Kind"/> is <see cref="AssetKind.Model"/>.</summary>
public interface IModelExporter : IExporter
{
    AssetKind IExporter.Kind => AssetKind.Model;

    ExportResult Export(Model model, ExportContext context);
}
