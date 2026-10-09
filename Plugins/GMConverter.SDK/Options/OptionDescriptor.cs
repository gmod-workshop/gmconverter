namespace GMConverter.SDK.Options;

/// <summary>
/// Describes a single configurable option an importer or exporter accepts. The host uses this to
/// render UI controls, register CLI arguments, persist values, and validate before invoking the
/// plugin.
/// </summary>
/// <param name="Key">
/// Stable identifier used to look the value up in <see cref="OptionValues"/>. Convention is to
/// prefix related options with a group-like tag (e.g. <c>"physics:mode"</c>) when the option
/// logically belongs to a sub-feature, but the schema's <see cref="OptionGroup"/> structure is
/// the source of truth for UI grouping.
/// </param>
/// <param name="Type">Logical type used for rendering and parsing.</param>
/// <param name="Label">Human-readable label shown in UI.</param>
public sealed record OptionDescriptor(string Key, OptionType Type, string Label)
{
    /// <summary>
    /// Default value used when the host has nothing persisted for this key. May be <c>null</c>
    /// (especially for <see cref="OptionType.String"/> and <see cref="OptionType.Path"/> where
    /// "unset" is a valid state).
    /// </summary>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// Optional deferred default — invoked lazily by <see cref="ResolveDefault"/>. Use this when
    /// the default needs to be computed at runtime (e.g. discovering an installed tool path on
    /// the user's machine) rather than being known statically.
    /// </summary>
    public Func<object?>? DefaultValueFactory { get; init; }

    /// <summary>Optional human-readable help text shown alongside the control.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// For <see cref="OptionType.Enum"/>: the allowed values. The host's enum control (combo box,
    /// CLI choice constraint) restricts selection to this set. Required when Type is Enum.
    /// </summary>
    public IReadOnlyList<string>? Choices { get; init; }

    /// <summary>Optional lower bound for numeric controls.</summary>
    public decimal? Minimum { get; init; }

    /// <summary>Optional upper bound for numeric controls.</summary>
    public decimal? Maximum { get; init; }

    /// <summary>Optional step size for numeric controls.</summary>
    public decimal? Increment { get; init; }

    /// <summary>
    /// Alternative names the host also accepts for this option, written as CLI flags without the
    /// leading dashes (e.g. <c>animation-path</c>). Lets a plugin keep an established flag or
    /// config key working when the option's own key or namespacing changes. For
    /// <see cref="OptionType.Bool"/> options, an alias that starts with <c>no-</c> sets the
    /// opposite value (e.g. <c>no-materials</c> turns <c>buildMaterials</c> off).
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>
    /// Resolves the effective default value at the time of the call. Calls
    /// <see cref="DefaultValueFactory"/> if set, otherwise returns <see cref="DefaultValue"/>.
    /// </summary>
    public object? ResolveDefault()
    {
        return DefaultValueFactory is not null ? DefaultValueFactory() : DefaultValue;
    }
}
