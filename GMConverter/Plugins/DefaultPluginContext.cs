using GMConverter.SDK.Explorer;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace GMConverter.Plugins;

internal sealed class DefaultPluginContext : IPluginContext
{
    private readonly List<IImporter> _importers = [];
    private readonly List<IExporterDescriptor> _exporters = [];
    private readonly List<IExplorer> _explorers = [];

    public DefaultPluginContext(IServiceProvider services)
    {
        Services = services;
    }

    public IServiceProvider Services { get; }

    public IReadOnlyList<IImporter> RegisteredImporters => _importers;

    public IReadOnlyList<IExporterDescriptor> RegisteredExporters => _exporters;

    public IReadOnlyList<IExplorer> RegisteredExplorers => _explorers;

    public void RegisterImporter(IImporter importer)
    {
        ArgumentNullException.ThrowIfNull(importer);
        _importers.Add(importer);
    }

    public void RegisterImporter<T>() where T : class, IImporter
    {
        _importers.Add(ActivatorUtilities.CreateInstance<T>(Services));
    }

    public void RegisterExporter(IExporterDescriptor exporter)
    {
        ArgumentNullException.ThrowIfNull(exporter);
        _exporters.Add(exporter);
    }

    public void RegisterExporter<T>() where T : class, IExporterDescriptor
    {
        _exporters.Add(ActivatorUtilities.CreateInstance<T>(Services));
    }

    public void RegisterExplorer(IExplorer explorer)
    {
        ArgumentNullException.ThrowIfNull(explorer);
        _explorers.Add(explorer);
    }

    public void RegisterExplorer<T>() where T : class, IExplorer
    {
        _explorers.Add(ActivatorUtilities.CreateInstance<T>(Services));
    }
}
