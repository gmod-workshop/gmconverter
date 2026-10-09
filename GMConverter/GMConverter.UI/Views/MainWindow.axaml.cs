using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GMConverter.UI.ViewModels;
using GMConverter.UI.Views;

namespace GMConverter.UI;

public partial class MainWindow : ShadUI.Window
{
    private const double _collapsedPreviewPaneWidth = 64;
    private const double _expandedPreviewPaneMinWidth = 280;
    private MainWindowViewModel? _shellViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachShellViewModel();
        SizeChanged += (_, _) => ApplyPreviewLayout();
        AttachShellViewModel();

        // Avalonia does not raise Unloaded on a top-level Window when it closes via the [X]
        // chrome button — the close path tears the window down without firing Unloaded on the
        // window itself or its descendants. Hooking Closed instead guarantees the disposal
        // chain runs; without it the SharpEngine SceneView's Vulkan resources keep a
        // foreground thread alive and the .NET host process strands holding the assembly DLLs
        // even after the window is gone, which blocks the next rebuild.
        Closed += (_, _) =>
        {
            foreach (var pane in this.GetVisualDescendants().OfType<PreviewPane>())
            {
                pane.Dispose();
            }

            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SaveSettingsNow();
                viewModel.Dispose();
            }
        };
    }

    private void AttachShellViewModel()
    {
        _shellViewModel?.PropertyChanged -= ShellViewModel_PropertyChanged;

        _shellViewModel = DataContext as MainWindowViewModel;

        _shellViewModel?.PropertyChanged += ShellViewModel_PropertyChanged;

        ApplyPreviewLayout();
    }

    private void ShellViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsPreviewPaneExpanded) or
            nameof(MainWindowViewModel.PreviewPaneRatio))
        {
            ApplyPreviewLayout();
        }
    }

    private void ApplyPreviewLayout()
    {
        if (_shellViewModel?.IsPreviewPaneExpanded == true)
        {
            ShellGrid.ColumnDefinitions[1].Width = new GridLength(1 - _shellViewModel.PreviewPaneRatio, GridUnitType.Star);
            ShellGrid.ColumnDefinitions[2].Width = new GridLength(6);
            ShellGrid.ColumnDefinitions[3].MinWidth = _expandedPreviewPaneMinWidth;
            ShellGrid.ColumnDefinitions[3].Width = new GridLength(_shellViewModel.PreviewPaneRatio, GridUnitType.Star);
            return;
        }

        ShellGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        ShellGrid.ColumnDefinitions[2].Width = new GridLength(0);
        ShellGrid.ColumnDefinitions[3].MinWidth = _collapsedPreviewPaneWidth;
        ShellGrid.ColumnDefinitions[3].Width = GridLength.Auto;
    }

    private void SwitchTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = Application.Current.ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

    private void ToggleFullscreen_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
    }

    private void PreviewResizeThumb_DragDelta(object? sender, VectorEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            var flexibleWidth = ShellGrid.ColumnDefinitions[1].ActualWidth + ShellGrid.ColumnDefinitions[3].ActualWidth;
            viewModel.AdjustPreviewPaneRatio(e.Vector.X, flexibleWidth);
            ApplyPreviewLayout();
        }
    }
}
