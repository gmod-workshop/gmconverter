using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.SDK.Options;
using GMConverter.UI.Services;
using GMConverter.UI.ViewModels.Options;

namespace GMConverter.UI.ViewModels;

/// <summary>
/// Per-importer option state. Each input format gets its own schema-driven option set, created
/// on first use and persisted by format so switching inputs never loses values.
/// </summary>
public sealed partial class ConvertViewModel
{
    private readonly Dictionary<string, OptionSetViewModel> _importerOptionsByFormat =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Dictionary<string, object?>> _persistedImporterOptions =
        new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private OptionSetViewModel _currentImporterOptions = new("", OptionSchema.Empty);

    /// <summary>Lookup or create the option set for the active input format and swap it in.</summary>
    private void RefreshCurrentImporterOptions()
    {
        var format = SelectedInputFormat?.Value;
        if (string.IsNullOrEmpty(format) || ConversionService.FindImporter(format) is not { } importer)
        {
            CurrentImporterOptions = new OptionSetViewModel("", OptionSchema.Empty);
            return;
        }

        if (!_importerOptionsByFormat.TryGetValue(format, out var vm))
        {
            vm = new OptionSetViewModel(format, importer.OptionSchema);
            if (_persistedImporterOptions.TryGetValue(format, out var persisted))
            {
                vm.LoadFrom(persisted);
            }
            // Re-raise edits as a change of the whole set so the settings file is saved.
            foreach (var option in vm.Groups.SelectMany(group => group.Options))
            {
                option.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CurrentImporterOptions));
            }
            _importerOptionsByFormat[format] = vm;
        }
        CurrentImporterOptions = vm;
    }

    /// <summary>
    /// Applies importer option values an explorer resolved for a browsed entry (e.g. an extracted
    /// animation) to the active input format. Keys the importer does not declare are ignored.
    /// </summary>
    internal void ApplyImporterOptions(IReadOnlyDictionary<string, object?>? values)
    {
        if (values is not null)
        {
            CurrentImporterOptions.LoadFrom(values);
        }
    }

    /// <summary>Resets the active importer's options to their schema defaults.</summary>
    private void ResetCurrentImporterOptions()
    {
        CurrentImporterOptions.LoadFrom(CurrentImporterOptions.Schema.AllOptions
            .ToDictionary(option => option.Key, option => option.ResolveDefault()));
    }

    internal Dictionary<string, Dictionary<string, object?>> SnapshotImporterOptions()
    {
        var snapshot = _persistedImporterOptions.ToDictionary(pair => pair.Key,
            pair => new Dictionary<string, object?>(pair.Value), StringComparer.OrdinalIgnoreCase);
        foreach (var (format, vm) in _importerOptionsByFormat)
        {
            snapshot[format] = vm.Snapshot();
        }
        return snapshot;
    }

    private void LoadImporterOptions(Dictionary<string, Dictionary<string, object?>>? persisted)
    {
        _persistedImporterOptions.Clear();
        if (persisted is not null)
        {
            foreach (var (format, values) in persisted)
            {
                _persistedImporterOptions[format] = new Dictionary<string, object?>(values);
            }
        }
        foreach (var (format, vm) in _importerOptionsByFormat)
        {
            if (_persistedImporterOptions.TryGetValue(format, out var values))
            {
                vm.LoadFrom(values);
            }
        }
    }

    /// <summary>
    /// Applies config-file keys that are not host settings to the active importer and exporter,
    /// matching option keys and aliases. Returns the keys neither recognised.
    /// </summary>
    private List<string> ApplyConfigOptionValues(IReadOnlyDictionary<string, string> values)
    {
        List<string> unmatched = [];
        foreach (var (key, value) in values)
        {
            var option = CurrentImporterOptions.FindByName(key, UiConfig.NormalizeKey)
                ?? CurrentExporterOptions.FindByName(key, UiConfig.NormalizeKey);
            if (option is null || !option.TryLoad(value))
            {
                unmatched.Add(key);
            }
        }
        return unmatched;
    }
}
