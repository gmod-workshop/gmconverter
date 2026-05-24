using Microsoft.Extensions.Logging;

namespace GMConverter.Plugins;

/// <summary>
/// Static bootstrap surface for the plugin system. Hosts (CLI, UI) call
/// <see cref="Initialize"/> once at startup, after which <see cref="Registry"/> exposes the
/// merged set of plugin-contributed importers/exporters/explorers. Until <see cref="Initialize"/>
/// runs, <see cref="Registry"/> returns <see cref="PluginRegistry.Empty"/> so call-sites can
/// safely consult it during tests or before the host's startup sequence reaches the plugin step.
/// </summary>
public static class PluginHost
{
    private static readonly Lock _lock = new();
    private static PluginRegistry? _registry;

    public static PluginRegistry Registry => _registry ?? PluginRegistry.Empty;

    /// <summary>
    /// The default plugin discovery root: <c>plugins/</c> next to the executing assembly.
    /// </summary>
    // Path.Join (vs Path.Combine) is unconditional concatenation — it does not interpret the
    // second argument as a potentially-rooted path that would drop the first. The literal
    // "plugins" cannot be rooted, but Path.Join silences the analyzer warning cleanly.
    public static string DefaultDirectory => Path.Join(AppContext.BaseDirectory, "plugins");

    /// <summary>
    /// Loads all plugins under <paramref name="pluginsDirectory"/>. Subsequent calls are
    /// no-ops and return the existing registry — the loader runs exactly once per process.
    /// </summary>
    public static PluginRegistry Initialize(string pluginsDirectory, ILoggerFactory? loggerFactory = null)
    {
        lock (_lock)
        {
            if (_registry is not null)
            {
                return _registry;
            }

            var loader = new PluginLoader(loggerFactory);
            _registry = loader.LoadAll(pluginsDirectory);
            return _registry;
        }
    }
}
