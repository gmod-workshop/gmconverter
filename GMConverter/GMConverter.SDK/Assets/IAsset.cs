namespace GMConverter.SDK.Assets;

/// <summary>An engine-neutral asset that importers produce and exporters consume.</summary>
public interface IAsset
{
    /// <summary>The asset's name, used for default output names and messages.</summary>
    string Name { get; }

    AssetKind Kind { get; }
}
