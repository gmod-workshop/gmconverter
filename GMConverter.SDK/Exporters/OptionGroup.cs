namespace GMConverter.SDK.Exporters;

/// <summary>
/// A logical grouping of related options — rendered as a tab, expander, or labelled section
/// depending on host. Keys must be unique within an exporter's schema.
/// </summary>
/// <param name="Key">Stable group identifier (e.g. <c>"tools"</c>, <c>"physics"</c>).</param>
/// <param name="Label">Human-readable header shown in the UI.</param>
/// <param name="Options">Options that belong to this group.</param>
public sealed record OptionGroup(
    string Key,
    string Label,
    IReadOnlyList<OptionDescriptor> Options);
