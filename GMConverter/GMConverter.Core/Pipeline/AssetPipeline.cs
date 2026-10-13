using GMConverter.SDK.Assets;
using GMConverter.SDK.Common;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Options;

namespace GMConverter.Core.Pipeline;

/// <summary>
/// Runs a request through the steps every asset kind shares, in order: resolve the destination
/// (so a bad output setting fails before a slow import), import, then export. Subclasses
/// implement the import and export steps for their asset type.
/// </summary>
internal abstract class AssetPipeline<TAsset> : IAssetPipeline
    where TAsset : class, IAsset
{
    public abstract AssetKind Kind { get; }

    public AssetResult Run(AssetRequest request)
    {
        var destination = AssetDestination.Resolve(request);
        if (destination.Format.Exporter.Kind != Kind)
        {
            throw new GMConverterException(
                $"{destination.Format.Format} output writes a {destination.Format.Exporter.Kind}, not a {Kind}.");
        }

        var asset = Import(request);
        var exported = Export(asset, request, destination);
        return new AssetResult(request, destination, exported);
    }

    /// <summary>Reads the request's input into an asset. Also used on its own by previews.</summary>
    public abstract TAsset Import(AssetRequest request);

    protected abstract ExportResult Export(TAsset asset, AssetRequest request, AssetDestination destination);

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
