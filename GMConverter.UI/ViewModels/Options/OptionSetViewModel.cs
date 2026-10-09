using System.Collections.ObjectModel;
using GMConverter.SDK.Options;

namespace GMConverter.UI.ViewModels.Options;

/// <summary>
/// Schema-driven options state for a single importer or exporter. The generic
/// <c>OptionsPanel.axaml</c> binds to this and renders one section per group. The host builds an
/// <see cref="OptionValues"/> bag by snapshotting current values via
/// <see cref="BuildOptionValues"/> at conversion time.
/// </summary>
public sealed class OptionSetViewModel
{
    private readonly Dictionary<string, object?> _defaults;

    /// <summary>The input or output format whose schema this set renders.</summary>
    public string Format { get; }

    public OptionSchema Schema { get; }

    public ObservableCollection<OptionGroupViewModel> Groups { get; }

    public bool HasOptions => Groups.Count > 0;

    public OptionSetViewModel(string format, OptionSchema schema)
    {
        Format = format;
        Schema = schema;
        Groups = [.. schema.Groups.Select(group => new OptionGroupViewModel(group))];
        _defaults = Snapshot();
    }

    /// <summary>Snapshot every option's current value into an <see cref="OptionValues"/> bag.</summary>
    public OptionValues BuildOptionValues()
    {
        return new OptionValues(Snapshot());
    }

    /// <summary>
    /// Returns a dict of all current option values keyed by option key. Used by the persistence
    /// layer (UiSettings) to serialize per-format state to disk.
    /// </summary>
    public Dictionary<string, object?> Snapshot()
    {
        var snapshot = new Dictionary<string, object?>();
        foreach (var group in Groups)
        {
            foreach (var option in group.Options)
            {
                snapshot[option.Key] = option.GetCurrentValue();
            }
        }
        return snapshot;
    }

    /// <summary>
    /// The values that differ from the schema defaults. This is what the host persists, so a
    /// default that is computed at runtime (such as a discovered tool path) is never saved and
    /// changed defaults reach users who never edited the option.
    /// </summary>
    public Dictionary<string, object?> SnapshotChanged()
    {
        return Snapshot()
            .Where(pair => !Equals(pair.Value, _defaults.GetValueOrDefault(pair.Key)))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>Puts every option back to the value it had when this set was created.</summary>
    public void ResetToDefaults()
    {
        LoadFrom(_defaults);
    }

    /// <summary>
    /// Replace current values from a persisted dict. Keys not in the schema are ignored;
    /// schema keys not in the dict keep their default values.
    /// </summary>
    public void LoadFrom(IReadOnlyDictionary<string, object?> persisted)
    {
        foreach (var option in Groups.SelectMany(g => g.Options).Where(option => persisted.ContainsKey(option.Key)))
        {
            option.TryLoad(persisted[option.Key]);
        }
    }

    /// <summary>
    /// Finds the option whose key or one of whose aliases matches <paramref name="name"/> after
    /// <paramref name="normalize"/> is applied to both sides. Used to map config-file keys.
    /// </summary>
    public OptionViewModel? FindByName(string name, Func<string, string> normalize)
    {
        var target = normalize(name);
        return Groups
            .SelectMany(group => group.Options)
            .FirstOrDefault(option => normalize(option.Key) == target ||
                option.Aliases.Any(alias => normalize(alias) == target));
    }
}
