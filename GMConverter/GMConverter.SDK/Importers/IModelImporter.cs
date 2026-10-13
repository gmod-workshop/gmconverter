using GMConverter.SDK.Assets;
using GMConverter.SDK.Geometry;

namespace GMConverter.SDK.Importers;

/// <summary>An importer that reads a file into a <see cref="Model"/>. Its <see cref="IImporter.Kind"/> is <see cref="AssetKind.Model"/>.</summary>
public interface IModelImporter : IImporter
{
    AssetKind IImporter.Kind => AssetKind.Model;

    Model Import(ModelImportContext context);
}
