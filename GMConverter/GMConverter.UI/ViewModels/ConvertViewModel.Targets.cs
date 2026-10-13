using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.Core.Formats;
using GMConverter.Core.Games;
using GMConverter.SDK.Options;
using GMConverter.UI.Models;
using GMConverter.UI.ViewModels.Options;

namespace GMConverter.UI.ViewModels;

/// <summary>
/// Target game state. An exporter can offer several games to export for (Garry's Mod or another
/// Source game for MDL); the choice is remembered per exporter, and each game's own options are
/// kept and persisted by game id, like exporter options are by format.
/// </summary>
public sealed partial class ConvertViewModel
{
    private readonly Dictionary<string, string> _targetByExporter = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, OptionSetViewModel> _targetOptionsById = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Dictionary<string, object?>> _persistedTargetOptions =
        new(StringComparer.OrdinalIgnoreCase);

    // Rebuilding Targets makes the combo box clear its selection; that must not be remembered.
    private bool _refreshingTargets;

    [ObservableProperty]
    private DisplayOption? _selectedTarget;

    [ObservableProperty]
    private OptionSetViewModel _currentTargetOptions = new("", OptionSchema.Empty);

    /// <summary>The games the selected output format can export for; empty when it has none.</summary>
    public ObservableCollection<DisplayOption> Targets { get; } = [];

    public bool HasTargets => Targets.Count > 0;

    partial void OnSelectedTargetChanged(DisplayOption? value)
    {
        if (!_refreshingTargets && value is not null && CurrentExporterFormat() is { } exporter)
        {
            _targetByExporter[exporter] = value.Value;
        }
        RefreshCurrentTargetOptions();
    }

    /// <summary>Lists the selected output format's targets and selects the remembered or default one.</summary>
    private void RefreshTargets()
    {
        var exporter = CurrentExporterFormat();
        var targets = exporter is null ? [] : GameCatalog.TargetsFor(exporter);

        _refreshingTargets = true;
        try
        {
            Targets.Clear();
            foreach (var target in targets)
            {
                Targets.Add(new DisplayOption(target.Id, target.DisplayName, target.Engine));
            }

            var remembered = exporter is not null && _targetByExporter.TryGetValue(exporter, out var id) ? id : null;
            SelectedTarget = Targets.FirstOrDefault(target => string.Equals(target.Value, remembered, StringComparison.OrdinalIgnoreCase))
                ?? Targets.FirstOrDefault();
        }
        finally
        {
            _refreshingTargets = false;
        }

        OnPropertyChanged(nameof(HasTargets));
        RefreshCurrentTargetOptions();
    }

    private void RefreshCurrentTargetOptions()
    {
        var exporter = CurrentExporterFormat();
        var target = SelectedTarget is null || exporter is null
            ? null
            : GameCatalog.TargetsFor(exporter).FirstOrDefault(candidate => string.Equals(candidate.Id, SelectedTarget.Value, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            CurrentTargetOptions = new OptionSetViewModel("", OptionSchema.Empty);
            return;
        }

        var targetId = target.Id;
        if (!_targetOptionsById.TryGetValue(targetId, out var vm))
        {
            vm = new OptionSetViewModel(targetId, target.OptionSchema);
            if (_persistedTargetOptions.TryGetValue(targetId, out var persisted))
            {
                vm.LoadFrom(persisted);
            }
            // Re-raise edits as a change of the whole set so the settings file is saved.
            foreach (var option in vm.Groups.SelectMany(group => group.Options))
            {
                option.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CurrentTargetOptions));
            }
            _targetOptionsById[targetId] = vm;
        }
        CurrentTargetOptions = vm;
    }

    private string? CurrentExporterFormat()
    {
        var selected = SelectedOutputFormat?.Value;
        return string.IsNullOrEmpty(selected) ? null : FormatCatalog.FindExport(selected)?.Exporter.Format;
    }

    internal Dictionary<string, string> SnapshotTargets()
    {
        return new Dictionary<string, string>(_targetByExporter, StringComparer.OrdinalIgnoreCase);
    }

    internal Dictionary<string, Dictionary<string, object?>> SnapshotTargetOptions()
    {
        var snapshot = _persistedTargetOptions.ToDictionary(pair => pair.Key,
            pair => new Dictionary<string, object?>(pair.Value), StringComparer.OrdinalIgnoreCase);
        foreach (var (targetId, vm) in _targetOptionsById)
        {
            snapshot[targetId] = vm.SnapshotChanged();
        }
        return snapshot;
    }

    private void LoadTargets(Dictionary<string, string>? targets, Dictionary<string, Dictionary<string, object?>>? targetOptions)
    {
        _targetByExporter.Clear();
        foreach (var (exporter, targetId) in targets ?? [])
        {
            _targetByExporter[exporter] = targetId;
        }

        _persistedTargetOptions.Clear();
        foreach (var (targetId, values) in targetOptions ?? [])
        {
            _persistedTargetOptions[targetId] = new Dictionary<string, object?>(values);
        }
        foreach (var (targetId, vm) in _targetOptionsById)
        {
            if (_persistedTargetOptions.TryGetValue(targetId, out var values))
            {
                vm.LoadFrom(values);
            }
        }

        RefreshTargets();
    }
}
