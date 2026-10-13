using GMConverter.SDK.Options;

namespace GMConverter.Core.Pipeline;

/// <summary>
/// Runs a request through the steps every asset kind shares, in order: resolve the destination
/// (so a bad output setting fails before a slow import), import, then export. Subclasses
/// implement the import and export steps for their asset type.
/// </summary>
internal abstract class AssetPipeline<TAsset> : IAssetPipeline
    where TAsset : class
{
    public abstract bool CanRun(AssetRequest request);

    public AssetResult Run(AssetRequest request)
    {
        var destination = AssetDestination.Resolve(request);
        var asset = Import(request);
        Export(asset, request, destination);
        return new AssetResult(request, destination);
    }

    /// <summary>Reads the request's input into an asset. Also used on its own by previews.</summary>
    public abstract TAsset Import(AssetRequest request);

    protected abstract void Export(TAsset asset, AssetRequest request, AssetDestination destination);

    /// <summary>Schema defaults first, then the values the request set explicitly.</summary>
    protected static OptionValues WithDefaults(OptionSchema schema, OptionValues overrides)
    {
        var values = schema.AllOptions.ToDictionary(option => option.Key, option => option.ResolveDefault());
        foreach (var (key, value) in overrides.AsDictionary())
        {
            values[key] = value;
        }
        return new OptionValues(values);
    }
}
