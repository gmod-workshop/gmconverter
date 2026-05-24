using System.Reflection;
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

    public PluginLoadContext(string pluginAssemblyPath, string pluginId)
        : base(name: $"Plugin:{pluginId}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
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
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
