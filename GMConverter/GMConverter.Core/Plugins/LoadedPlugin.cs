using GMConverter.SDK.Plugins;

namespace GMConverter.Core.Plugins;

internal sealed record LoadedPlugin(
    PluginManifest Manifest,
    IPlugin Instance,
    PluginLoadContext LoadContext,
    DefaultPluginContext Context);
