using GMConverter.SDK.Explorer;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Plugins;

namespace GMConverter.Plugins;

/// <summary>
/// Aggregated view of importers, exporters, and explorers contributed by loaded plugins. The
/// host's built-in routing is consulted first; this registry is queried as a fallback for
/// formats / targets the built-ins do not handle.
/// </summary>
public sealed class PluginRegistry
{
    private readonly List<LoadedPlugin> _plugins = [];
    private readonly Dictionary<string, IImporter> _importersByFormat = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IExporterDescriptor> _exportersByFormat = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IExplorer> _explorers = [];

    public static PluginRegistry Empty { get; } = new();

    public IReadOnlyList<PluginManifest> LoadedPlugins => _plugins.Select(p => p.Manifest).ToArray();

    public IReadOnlyList<IExplorer> Explorers => _explorers;

    public IImporter? GetImporter(string inputFormat) =>
        _importersByFormat.TryGetValue(inputFormat, out var importer) ? importer : null;

    public IExporterDescriptor? GetExporter(string outputFormat) =>
        _exportersByFormat.TryGetValue(outputFormat, out var exporter) ? exporter : null;

    internal void Add(LoadedPlugin loaded)
    {
        _plugins.Add(loaded);
        foreach (var importer in loaded.Context.RegisteredImporters)
        {
            _importersByFormat[importer.InputFormat] = importer;
        }
        foreach (var exporter in loaded.Context.RegisteredExporters)
        {
            _exportersByFormat[exporter.OutputFormat] = exporter;
        }
        _explorers.AddRange(loaded.Context.RegisteredExplorers);
    }
}
