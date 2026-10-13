using GMConverter.SDK.Assets;
using GMConverter.SDK.Audio;

namespace GMConverter.SDK.Exporters;

/// <summary>
/// An exporter that writes a <see cref="Sound"/>: its clips in the target's audio format and its
/// events as the target's sound definitions. Its <see cref="IExporter.Kind"/> is <see cref="AssetKind.Sound"/>.
/// </summary>
public interface ISoundExporter : IExporter
{
    AssetKind IExporter.Kind => AssetKind.Sound;

    ExportResult Export(Sound sound, ExportContext context);
}
