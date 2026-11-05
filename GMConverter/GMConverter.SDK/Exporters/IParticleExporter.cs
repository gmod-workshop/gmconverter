using GMConverter.SDK.Assets;
using GMConverter.SDK.Particles;

namespace GMConverter.SDK.Exporters;

/// <summary>
/// An exporter that writes a <see cref="ParticleEffect"/> in the target's particle format, listing what
/// it couldn't express in <see cref="ExportResult.Warnings"/>. Its <see cref="IExporter.Kind"/> is
/// <see cref="AssetKind.Particle"/>.
/// </summary>
public interface IParticleExporter : IExporter
{
    AssetKind IExporter.Kind => AssetKind.Particle;

    ExportResult Export(ParticleEffect effect, ExportContext context);
}
