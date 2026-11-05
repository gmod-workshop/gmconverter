using GMConverter.SDK.Assets;
using GMConverter.SDK.Particles;

namespace GMConverter.SDK.Importers;

/// <summary>An importer that reads a file into a <see cref="ParticleEffect"/>. Its <see cref="IImporter.Kind"/> is <see cref="AssetKind.Particle"/>.</summary>
public interface IParticleImporter : IImporter
{
    AssetKind IImporter.Kind => AssetKind.Particle;

    ParticleEffect Import(ParticleImportContext context);
}
