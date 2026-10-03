using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GMConverter.UI.ViewModels.Options;

namespace GMConverter.UI.Controls.Settings;

/// <summary>
/// Generic schema-driven options panel. Renders one section per <see cref="OptionGroupViewModel"/>
/// in the bound <see cref="ExporterOptionsViewModel"/>, with controls per option type chosen via
/// the <c>DataTemplate</c>s in the XAML resources. The <see cref="Groups"/> property filters which
/// group keys to render — empty / null renders all groups; a comma-separated value restricts to
/// those keys (in declaration order). Used by both <c>SettingsView</c> (tools + materials) and
/// <c>ConvertView</c> (physics) to render different slices of the same exporter schema.
/// </summary>
public partial class ExporterOptionsPanel : UserControl
{
    public static readonly StyledProperty<string?> GroupsProperty =
        AvaloniaProperty.Register<ExporterOptionsPanel, string?>(nameof(Groups));

    /// <summary>
    /// Comma-separated list of group keys to render. <c>null</c> or empty renders every group.
    /// Unknown keys are silently ignored.
    /// </summary>
    public string? Groups
    {
        get => GetValue(GroupsProperty);
        set => SetValue(GroupsProperty, value);
    }

    /// <summary>
    /// Computed view bound by the inner ItemsControl. Refreshed whenever the DataContext or the
    /// <see cref="Groups"/> filter changes.
    /// </summary>
    public ObservableCollection<OptionGroupViewModel> FilteredGroups { get; } = [];

    public ExporterOptionsPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RebuildFilteredGroups();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == GroupsProperty)
            {
                RebuildFilteredGroups();
            }
        };
        RebuildFilteredGroups();
    }

    private void RebuildFilteredGroups()
    {
        FilteredGroups.Clear();
        if (DataContext is not ExporterOptionsViewModel vm)
        {
            return;
        }

        var filter = string.IsNullOrWhiteSpace(Groups)
            ? null
            : Groups.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var group in vm.Groups)
        {
            if (filter is null || filter.Contains(group.Key, StringComparer.OrdinalIgnoreCase))
            {
                FilteredGroups.Add(group);
            }
        }
    }

    private async void BrowsePath_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not PathOptionViewModel optionVm)
        {
            return;
        }
        var window = TopLevel.GetTopLevel(this);
        if (window is null)
        {
            return;
        }

        var result = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select {optionVm.Label}",
            AllowMultiple = false,
        });
        if (result is { Count: > 0 } && result[0].TryGetLocalPath() is { } path)
        {
            optionVm.Value = path;
        }
    }
}
