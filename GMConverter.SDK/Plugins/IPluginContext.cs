using GMConverter.SDK.Explorer;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Importers;

namespace GMConverter.SDK.Plugins;

/// <summary>
/// Host-provided context handed to a plugin during <see cref="IPlugin.OnLoad"/>. Exposes the
/// host's service provider and registration hooks for the contract types the plugin contributes.
/// </summary>
/// <remarks>
/// Plugins consume host services (texture factory, logger factory, anything the host registers
/// in the future) through <see cref="Services"/>. The generic <c>Register*&lt;T&gt;</c>
/// overloads activate the type via <c>ActivatorUtilities</c>, resolving constructor parameters
/// from the same provider — so an importer with a dependency on <c>ITextureFactory</c> just
/// declares it as a ctor parameter and the host wires it. The non-generic overloads accept a
/// pre-constructed instance for the rare case where the plugin needs full control over
/// construction (custom factory, decorator pattern, etc.).
/// </remarks>
public interface IPluginContext
{
    /// <summary>
    /// Service provider populated with host-side services. Plugin code is expected to consume
    /// services by declaring constructor parameters and letting the
    /// <c>Register*&lt;T&gt;()</c> overloads resolve them; direct access via
    /// <c>Services.GetRequiredService&lt;T&gt;()</c> is supported as an escape hatch.
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>
    /// Registers an importer instance with the host.
    /// </summary>
    void RegisterImporter(IImporter importer);

    /// <summary>
    /// Activates <typeparamref name="T"/> via <c>ActivatorUtilities.CreateInstance</c> against
    /// <see cref="Services"/> and registers the resulting importer.
    /// </summary>
    void RegisterImporter<T>() where T : class, IImporter;

    /// <summary>
    /// Registers an exporter instance with the host.
    /// </summary>
    void RegisterExporter(IExporter exporter);

    /// <summary>
    /// Activates <typeparamref name="T"/> via <c>ActivatorUtilities.CreateInstance</c> against
    /// <see cref="Services"/> and registers the resulting exporter.
    /// </summary>
    void RegisterExporter<T>() where T : class, IExporter;

    /// <summary>
    /// Registers an archive explorer (browser) instance with the host.
    /// </summary>
    void RegisterExplorer(IExplorer explorer);

    /// <summary>
    /// Activates <typeparamref name="T"/> via <c>ActivatorUtilities.CreateInstance</c> against
    /// <see cref="Services"/> and registers the resulting explorer.
    /// </summary>
    void RegisterExplorer<T>() where T : class, IExplorer;
}
