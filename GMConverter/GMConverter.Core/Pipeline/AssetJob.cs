namespace GMConverter.Core.Pipeline;

/// <summary>A set of assets converted together.</summary>
internal sealed record AssetJob(IReadOnlyList<AssetRequest> Requests)
{
    public AssetJob(AssetRequest request)
        : this([request])
    {
    }
}
