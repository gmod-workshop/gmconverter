using System.Collections.ObjectModel;
using GMConverter.SDK.Exporters;

namespace GMConverter.UI.ViewModels.Options;

/// <summary>
/// Schema-driven options state for a single exporter. The generic
/// <c>ExporterOptionsPanel.axaml</c> binds to this and renders one section per group. The host
/// builds an <see cref="ExportOptions"/> bag by snapshotting current values via
/// <see cref="BuildExportOptions"/> at conversion time.
/// </summary>
public sealed class ExporterOptionsViewModel
{
    public string ExporterFormat { get; }

    public ExporterOptionSchema Schema { get; }

    public ObservableCollection<OptionGroupViewModel> Groups { get; }

    public ExporterOptionsViewModel(string exporterFormat, ExporterOptionSchema schema)
    {
        ExporterFormat = exporterFormat;
        Schema = schema;
        Groups = [.. schema.Groups.Select(group => new OptionGroupViewModel(group))];
    }

    /// <summary>Snapshot every option's current value into an <see cref="ExportOptions"/> bag.</summary>
    public ExportOptions BuildExportOptions()
    {
        var bag = new Dictionary<string, object?>();
        foreach (var group in Groups)
        {
            foreach (var option in group.Options)
            {
                bag[option.Key] = option.GetCurrentValue();
            }
        }
        return new ExportOptions(bag);
    }

    /// <summary>
    /// Returns a dict of all current option values keyed by option key. Used by the persistence
    /// layer (UiSettings, UiConfig) to serialize per-exporter state to disk.
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
    /// Replace current values from a persisted dict. Keys not in the schema are ignored;
    /// schema keys not in the dict keep their default values.
    /// </summary>
    public void LoadFrom(IReadOnlyDictionary<string, object?> persisted)
    {
        foreach (var group in Groups)
        {
            foreach (var option in group.Options)
            {
                if (persisted.TryGetValue(option.Key, out var value))
                {
                    option.TryLoad(value);
                }
            }
        }
    }
}
