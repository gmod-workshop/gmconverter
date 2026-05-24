using GMConverter.SDK.Explorer;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Plugins;
using GMConverter.SDK.Textures;
using Microsoft.Extensions.Logging;

namespace GMConverter.Plugins;

internal sealed class DefaultPluginContext : IPluginContext
{
    private readonly List<IImporter> _importers = [];
    private readonly List<IExporterDescriptor> _exporters = [];
    private readonly List<IExplorer> _explorers = [];

    public DefaultPluginContext(ILoggerFactory loggerFactory, ITextureFactory textureFactory)
    {
        LoggerFactory = loggerFactory;
        TextureFactory = textureFactory;
    }

    public ILoggerFactory LoggerFactory { get; }

    public ITextureFactory TextureFactory { get; }

    public IReadOnlyList<IImporter> RegisteredImporters => _importers;

    public IReadOnlyList<IExporterDescriptor> RegisteredExporters => _exporters;

    public IReadOnlyList<IExplorer> RegisteredExplorers => _explorers;

    public void RegisterImporter(IImporter importer)
    {
        ArgumentNullException.ThrowIfNull(importer);
        _importers.Add(importer);
    }

    public void RegisterExporter(IExporterDescriptor exporter)
    {
        ArgumentNullException.ThrowIfNull(exporter);
        _exporters.Add(exporter);
    }

    public void RegisterExplorer(IExplorer explorer)
    {
        ArgumentNullException.ThrowIfNull(explorer);
        _explorers.Add(explorer);
    }
}
