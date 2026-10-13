using GMConverter.SDK.Assets;
using GMConverter.SDK.Audio;

namespace GMConverter.SDK.Importers;

/// <summary>An importer that reads a file into a <see cref="Sound"/>. Its <see cref="IImporter.Kind"/> is <see cref="AssetKind.Sound"/>.</summary>
public interface ISoundImporter : IImporter
{
    AssetKind IImporter.Kind => AssetKind.Sound;

    Sound Import(SoundImportContext context);
}
