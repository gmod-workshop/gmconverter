namespace GMConverter.SDK.Options;

/// <summary>
/// The full set of options an importer or exporter accepts, grouped for UI presentation. The host
/// renders one tab/section per group and one control per <see cref="OptionDescriptor"/>; the CLI
/// registers one argument per descriptor. The schema is the single source of truth for option
/// keys, labels, types, defaults, and (for <see cref="OptionType.Enum"/>) choices.
/// </summary>
public sealed record OptionSchema(IReadOnlyList<OptionGroup> Groups)
{
    /// <summary>An empty schema (no groups, no options). Returned by components with no settings.</summary>
    public static OptionSchema Empty { get; } = new([]);

    /// <summary>Flattened view of every option in the schema, regardless of group.</summary>
    public IEnumerable<OptionDescriptor> AllOptions => Groups.SelectMany(g => g.Options);

    /// <summary>Looks up an option by key, returning <c>null</c> if the key is not in the schema.</summary>
    public OptionDescriptor? Find(string key)
    {
        return AllOptions.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.Ordinal));
    }
}
