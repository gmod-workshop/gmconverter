namespace GMConverter.SDK.Plugins;

/// <summary>
/// Marks the <see cref="IPlugin"/> entry type for a plugin assembly. Apply at the assembly
/// level so the host can find the entry without scanning every type in the assembly:
/// <code>[assembly: Plugin(typeof(MyPlugin))]</code>
/// Plugins without this attribute still load — the host falls back to reflection-scanning
/// for a single <see cref="IPlugin"/> implementor — but the attribute is faster and explicit.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class PluginAttribute(Type entryType) : Attribute
{
    /// <summary>
    /// The concrete <see cref="IPlugin"/> implementation the host should instantiate.
    /// </summary>
    public Type EntryType { get; } = entryType;
}
