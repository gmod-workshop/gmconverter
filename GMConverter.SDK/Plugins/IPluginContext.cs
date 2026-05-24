using GMConverter.SDK.Explorer;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Textures;
using Microsoft.Extensions.Logging;

namespace GMConverter.SDK.Plugins;

/// <summary>
/// Host-provided context handed to a plugin during <see cref="IPlugin.OnLoad"/>. Exposes the
/// host's logger factory, the texture factory, and registration hooks for the contract types
/// the plugin contributes.
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// The host's logger factory. Plugins should use <c>LoggerFactory.CreateLogger&lt;T&gt;()</c>
    /// rather than constructing their own logger so that plugin output flows through the host's
    /// logging configuration.
    /// </summary>
    ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// The host's texture factory. Plugins use this to construct <see cref="Texture"/> instances
    /// without taking a dependency on a specific image library.
    /// </summary>
    ITextureFactory TextureFactory { get; }

    /// <summary>
    /// Registers an importer with the host. The host routes input files whose
    /// <see cref="IImporter.InputFormat"/> matches the importer's declared format through it.
    /// </summary>
    void RegisterImporter(IImporter importer);

    /// <summary>
    /// Registers an exporter with the host. Exporters are registered by their descriptor
    /// (<see cref="IExporterDescriptor.OutputFormat"/>); the host pattern-matches to the
    /// generic <see cref="IExporter{TOptions}"/> shape at use-time.
    /// </summary>
    void RegisterExporter(IExporterDescriptor exporter);

    /// <summary>
    /// Registers an archive explorer (browser) with the host. The host queries explorers in
    /// registration order via <see cref="IExplorer.Supports"/> to pick one for a given target.
    /// </summary>
    void RegisterExplorer(IExplorer explorer);
}
