using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.Core.Pipeline;
using GMConverter.SDK.Options;
using GMConverter.UI.ViewModels.Options;

namespace GMConverter.UI.ViewModels;

/// <summary>
/// Per-exporter option state. Each output format gets its own schema-driven option set, created
/// on first use and persisted by format, including formats whose plugin is currently missing.
/// </summary>
public sealed partial class ConvertViewModel
{
    private readonly Dictionary<string, OptionSetViewModel> _exporterOptionsByFormat =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Dictionary<string, object?>> _persistedExporterOptions =
        new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private OptionSetViewModel _currentExporterOptions =
        new("", OptionSchema.Empty);

    /// <summary>Lookup or create the option VM for the active output format and swap it in.</summary>
    private void RefreshCurrentExporterOptions()
    {
        var selected = SelectedOutputFormat?.Value;
        var format = string.IsNullOrEmpty(selected)
            ? selected
            : AssetFormats.FindExport(selected)?.OptionsKey ?? selected;
        if (string.IsNullOrEmpty(format))
        {
            CurrentExporterOptions = new OptionSetViewModel("", OptionSchema.Empty);
            return;
        }

        if (!_exporterOptionsByFormat.TryGetValue(format, out var vm))
        {
            vm = new OptionSetViewModel(format, GetSchemaFor(format));
            if (_persistedExporterOptions.TryGetValue(format, out var persisted))
            {
                vm.LoadFrom(persisted);
            }
            // Re-raise edits as a change of the whole set so the settings file is saved.
            foreach (var option in vm.Groups.SelectMany(group => group.Options))
            {
                option.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CurrentExporterOptions));
            }
            _exporterOptionsByFormat[format] = vm;
        }
        CurrentExporterOptions = vm;
    }

    private static OptionSchema GetSchemaFor(string format)
    {
        if (format == "info")
        {
            return OptionSchema.Empty;
        }
        // Options the format sets itself (glTF's binary/text choice) get no control.
        return AssetFormats.GetExport(format).OptionSchema;
    }

    internal Dictionary<string, Dictionary<string, object?>> SnapshotExporterOptions()
    {
        var snapshot = _persistedExporterOptions.ToDictionary(pair => pair.Key,
            pair => new Dictionary<string, object?>(pair.Value), StringComparer.OrdinalIgnoreCase);
        foreach (var (format, vm) in _exporterOptionsByFormat)
        {
            snapshot[format] = vm.SnapshotChanged();
        }
        return snapshot;
    }

    private void LoadExporterOptions(Dictionary<string, Dictionary<string, object?>>? persisted)
    {
        _persistedExporterOptions.Clear();
        if (persisted is not null)
        {
            foreach (var (format, values) in persisted)
            {
                _persistedExporterOptions[format] = new Dictionary<string, object?>(values);
            }
        }
        foreach (var (format, vm) in _exporterOptionsByFormat)
        {
            if (_persistedExporterOptions.TryGetValue(format, out var values))
            {
                vm.LoadFrom(values);
            }
        }
    }
}
