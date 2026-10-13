namespace GMConverter.Core.Pipeline;

/// <summary>The results of an <see cref="AssetJob"/>, one per request, in request order.</summary>
internal sealed record AssetJobResult(IReadOnlyList<AssetResult> Results);
