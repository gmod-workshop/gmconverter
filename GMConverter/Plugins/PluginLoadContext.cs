using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace GMConverter.Plugins;

/// <summary>
/// Collectible <see cref="AssemblyLoadContext"/> for a single plugin. The critical job is to
/// defer SDK assemblies back to the default (host) context so the same <c>Type</c> object
/// represents <c>IImporter</c>, <c>IExporter</c>, etc. on both sides of the plugin boundary —
/// otherwise plugin-supplied implementations would not satisfy the host's interface checks.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> _hostUnifiedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "GMConverter.SDK",
        "Microsoft.Extensions.Logging.Abstractions",
    };

    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginDirectory;

    public PluginLoadContext(string pluginAssemblyPath, string pluginId)
        : base(name: $"Plugin:{pluginId}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
        _pluginDirectory = Path.GetDirectoryName(pluginAssemblyPath)
            ?? throw new ArgumentException($"Plugin assembly path has no directory: {pluginAssemblyPath}", nameof(pluginAssemblyPath));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is { } name && _hostUnifiedAssemblies.Contains(name))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolved = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (resolved is not null)
        {
            return LoadUnmanagedDllFromPath(resolved);
        }

        // Fallback: probe the plugin's own folder. Some natives (notably CUE4Parse-Natives) ship
        // as Content/CopyToOutputDirectory items rather than a runtimes/<rid>/native/ layout, so
        // they end up next to the managed assemblies but aren't listed in deps.json. The default
        // host-context loader does search AppContext.BaseDirectory, but that's the host's bin,
        // not the plugin's subfolder — so we have to do the probe ourselves.
        foreach (var candidate in EnumerateNativeFileNames(unmanagedDllName))
        {
            var sibling = Path.Combine(_pluginDirectory, candidate);
            if (File.Exists(sibling))
            {
                return LoadUnmanagedDllFromPath(sibling);
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> EnumerateNativeFileNames(string unmanagedDllName)
    {
        // The runtime asks for the name in the form the P/Invoke source provided
        // ("CUE4Parse-Natives", "lib_coacd"). Try the bare name first, then platform-conventional
        // decorations. The set is platform-aware: Windows prefers ".dll" with no prefix; Linux
        // wants "lib" prefix + ".so"; macOS wants "lib" prefix + ".dylib".
        yield return unmanagedDllName;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return unmanagedDllName + ".dll";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            yield return unmanagedDllName + ".so";
            yield return "lib" + unmanagedDllName + ".so";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return unmanagedDllName + ".dylib";
            yield return "lib" + unmanagedDllName + ".dylib";
        }
    }
}
