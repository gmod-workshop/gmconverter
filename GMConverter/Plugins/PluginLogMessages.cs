using Microsoft.Extensions.Logging;

namespace GMConverter.Plugins;

internal static partial class PluginLogMessages
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Debug,
        Message = "Plugins directory not found, skipping discovery: {Directory}")]
    public static partial void PluginsDirectoryNotFound(this ILogger logger, string directory);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Loaded plugin {Id} v{Version}")]
    public static partial void PluginLoaded(this ILogger logger, string id, string version);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Error,
        Message = "Failed to load plugin from {ManifestPath}")]
    public static partial void PluginLoadFailed(this ILogger logger, Exception exception, string manifestPath);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "Could not read plugin manifest: {ManifestPath}")]
    public static partial void ManifestReadFailed(this ILogger logger, Exception exception, string manifestPath);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Warning,
        Message = "Plugin {Id} entry assembly missing: {EntryPath}")]
    public static partial void EntryAssemblyMissing(this ILogger logger, string id, string entryPath);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Error,
        Message = "Plugin {Id} entry assembly could not be loaded.")]
    public static partial void EntryAssemblyLoadFailed(this ILogger logger, Exception exception, string id);

    [LoggerMessage(
        EventId = 2006,
        Level = LogLevel.Warning,
        Message = "Plugin {Id}: no IPlugin entry type found via [assembly: Plugin(...)] or type scan.")]
    public static partial void NoEntryTypeFound(this ILogger logger, string id);

    [LoggerMessage(
        EventId = 2007,
        Level = LogLevel.Error,
        Message = "Plugin {Id}: entry type {TypeName} could not be instantiated.")]
    public static partial void EntryTypeInstantiationFailed(this ILogger logger, Exception exception, string id, string? typeName);

    [LoggerMessage(
        EventId = 2008,
        Level = LogLevel.Error,
        Message = "Plugin {Id} threw during OnLoad; not registering.")]
    public static partial void OnLoadThrew(this ILogger logger, Exception exception, string id);

    [LoggerMessage(
        EventId = 2009,
        Level = LogLevel.Error,
        Message = "Plugin {Id} also threw during OnUnload cleanup.")]
    public static partial void OnUnloadThrew(this ILogger logger, Exception exception, string id);

    [LoggerMessage(
        EventId = 2010,
        Level = LogLevel.Warning,
        Message = "Plugin {Id}: partial type-load failure during entry scan; using loadable subset.")]
    public static partial void PartialTypeLoadFailure(this ILogger logger, Exception exception, string id);

    [LoggerMessage(
        EventId = 2011,
        Level = LogLevel.Warning,
        Message = "Plugin {Id}: entry path '{Entry}' is rooted or escapes the plugin directory; skipping.")]
    public static partial void EntryPathEscapedPluginDirectory(this ILogger logger, string id, string entry);
}
