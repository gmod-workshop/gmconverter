using GMConverter.SDK.Exporters;

namespace GMConverter.Core.Pipeline;

/// <summary>What one <see cref="AssetRequest"/> wrote.</summary>
/// <param name="Request">The request that ran.</param>
/// <param name="Destination">Where its asset was written.</param>
/// <param name="Export">What the exporter reported writing.</param>
internal sealed record AssetResult(AssetRequest Request, AssetDestination Destination, ExportResult Export);
