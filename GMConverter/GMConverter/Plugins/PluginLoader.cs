using System.Reflection;
using System.Text.Json;
using GMConverter.SDK.Common;
using GMConverter.SDK.Plugins;
using GMConverter.SDK.Textures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GMConverter.Plugins;

/// <summary>
/// Discovers and loads plugins from a directory. Each plugin lives under its own subdirectory
/// containing a <c>plugin.json</c> manifest and the entry assembly named by the manifest's
/// <c>entry</c> field. Loaded plugins are returned wrapped in a <see cref="PluginRegistry"/>.
/// </summary>
public sealed class PluginLoader
{
    private const string _manifestFileName = "plugin.json";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PluginLoader> _logger;
    private readonly ServiceProvider _services;

    public PluginLoader(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<PluginLoader>();

        // The host's service provider — populated with everything plugin code can consume via DI.
        // Adding a new host-side capability (IFileSystem, IHttpClient, etc.) is a single AddSingleton
        // call here; existing plugins that don't need it are unaffected, and plugins that want it
        // just declare it as a constructor parameter.
        var services = new ServiceCollection();
        services.AddSingleton(_loggerFactory);
        services.AddSingleton<ITextureFactory, DefaultTextureFactory>();
        _services = services.BuildServiceProvider();
    }

    public PluginRegistry LoadAll(string pluginsDirectory)
    {
        var registry = new PluginRegistry();
        if (!Directory.Exists(pluginsDirectory))
        {
            _logger.PluginsDirectoryNotFound(pluginsDirectory);
            return registry;
        }

        foreach (var manifestPath in SafelyEnumerateManifests(pluginsDirectory))
        {
            try
            {
                if (TryLoadPlugin(manifestPath, out var loaded))
                {
                    registry.Add(loaded);
                    _logger.PluginLoaded(loaded.Manifest.Id, loaded.Manifest.Version);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _logger.PluginLoadFailed(ex, manifestPath);
            }
        }

        return registry;
    }

    private static string[] SafelyEnumerateManifests(string root)
    {
        try
        {
            return [.. Directory.EnumerateFiles(root, _manifestFileName, SearchOption.AllDirectories)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
            return [];
        }
    }

    private bool TryLoadPlugin(string manifestPath, out LoadedPlugin loaded)
    {
        loaded = null!;
        var manifest = ReadManifest(manifestPath);
        if (manifest is null)
        {
            return false;
        }

        var pluginDir = Path.GetDirectoryName(manifestPath)
            ?? throw new GMConverterException($"Plugin manifest has no directory: {manifestPath}");

        // Resolve the manifest's entry path against the plugin directory and reject anything that
        // is rooted, that uses ".." to escape the plugin folder, or that otherwise fails to
        // normalize cleanly. The manifest is data loaded from disk, so we treat it as untrusted —
        // a malformed or malicious plugin.json must not be able to point the loader at an
        // arbitrary assembly elsewhere on the filesystem.
        if (!PathHelpers.TryResolveUnderRoot(pluginDir, manifest.Entry, out var entryPath))
        {
            _logger.EntryPathEscapedPluginDirectory(manifest.Id, manifest.Entry);
            return false;
        }

        if (!File.Exists(entryPath))
        {
            _logger.EntryAssemblyMissing(manifest.Id, entryPath);
            return false;
        }

        var alc = new PluginLoadContext(entryPath, manifest.Id);
        Assembly assembly;
        try
        {
            assembly = alc.LoadFromAssemblyPath(entryPath);
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException)
        {
            _logger.EntryAssemblyLoadFailed(ex, manifest.Id);
            alc.Unload();
            return false;
        }

        var entryType = FindEntryType(assembly, manifest.Id);
        if (entryType is null)
        {
            _logger.NoEntryTypeFound(manifest.Id);
            alc.Unload();
            return false;
        }

        IPlugin instance;
        try
        {
            instance = (IPlugin)(Activator.CreateInstance(entryType)
                ?? throw new GMConverterException($"Activator returned null for {entryType.FullName}."));
        }
        catch (Exception ex) when (ex is TargetInvocationException or MissingMethodException or MemberAccessException)
        {
            _logger.EntryTypeInstantiationFailed(ex, manifest.Id, entryType.FullName);
            alc.Unload();
            return false;
        }

        var context = new DefaultPluginContext(_services);
        try
        {
            instance.OnLoad(context);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _logger.OnLoadThrew(ex, manifest.Id);
            try
            {
                instance.OnUnload();
            }
            catch (Exception unloadEx) when (unloadEx is not OutOfMemoryException and not StackOverflowException)
            {
                _logger.OnUnloadThrew(unloadEx, manifest.Id);
            }
            alc.Unload();
            return false;
        }

        loaded = new LoadedPlugin(manifest, instance, alc, context);
        return true;
    }

    private PluginManifest? ReadManifest(string manifestPath)
    {
        try
        {
            using var stream = File.OpenRead(manifestPath);
            return JsonSerializer.Deserialize<PluginManifest>(stream, _jsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.ManifestReadFailed(ex, manifestPath);
            return null;
        }
    }

    private Type? FindEntryType(Assembly assembly, string pluginId)
    {
        var attribute = assembly.GetCustomAttribute<PluginAttribute>();
        if (attribute is not null)
        {
            return attribute.EntryType;
        }

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            _logger.PartialTypeLoadFailure(ex, pluginId);
            types = [.. ex.Types.OfType<Type>()];
        }

        return types.FirstOrDefault(type =>
            !type.IsAbstract && !type.IsInterface && typeof(IPlugin).IsAssignableFrom(type));
    }
}
