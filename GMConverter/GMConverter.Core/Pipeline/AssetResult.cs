namespace GMConverter.Core.Pipeline;

/// <summary>What one <see cref="AssetRequest"/> wrote.</summary>
internal sealed record AssetResult(AssetRequest Request, AssetDestination Destination);
