namespace GMConverter.SDK.Plugins;

/// <summary>
/// Entry-point contract for a GMConverter plugin. Each plugin assembly contains exactly one
/// concrete <see cref="IPlugin"/> implementation marked with <see cref="PluginAttribute"/> at
/// the assembly level (or discovered by scanning, if the attribute is omitted). The host
/// instantiates it with a parameterless constructor, then calls <see cref="OnLoad"/> once.
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Stable identifier. Should match the <c>id</c> field in <c>plugin.json</c> and follow a
    /// reverse-domain or lowercase-kebab convention, e.g. <c>gmconverter.unrealengine</c>.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Human-readable name shown in plugin manager UIs.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Called once after the plugin assembly is loaded. Register importers, exporters, and
    /// explorers via <paramref name="context"/>. Exceptions thrown from this method are
    /// caught by the loader, logged, and prevent the plugin from being added to the registry.
    /// </summary>
    void OnLoad(IPluginContext context);

    /// <summary>
    /// Called once when the plugin is being unloaded (e.g. before the host shuts down or when
    /// the user uninstalls). Release any owned resources.
    /// </summary>
    void OnUnload();
}
