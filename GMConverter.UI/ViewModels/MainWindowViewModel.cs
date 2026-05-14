using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GMConverter.Common;
using GMConverter.Explorer;
using GMConverter.UI.Services;

namespace GMConverter.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const double _minPreviewPaneRatio = 0.2;
    private const double _maxPreviewPaneRatio = 0.4;

    private static readonly HashSet<string> _convertSettingsProperties = new(StringComparer.Ordinal)
    {
        nameof(ConvertViewModel.SelectedInputFormat),
        nameof(ConvertViewModel.SelectedOutputFormat),
        nameof(ConvertViewModel.SelectedAxisMode),
        nameof(ConvertViewModel.SelectedPhysicsMode),
        nameof(ConvertViewModel.ConfigPath),
        nameof(ConvertViewModel.InputPath),
        nameof(ConvertViewModel.AnimationPath),
        nameof(ConvertViewModel.OutputPath),
        nameof(ConvertViewModel.BaseName),
        nameof(ConvertViewModel.ModelPath),
        nameof(ConvertViewModel.StudioMdlPath),
        nameof(ConvertViewModel.VtfCmdPath),
        nameof(ConvertViewModel.MaterialDirectory),
        nameof(ConvertViewModel.ScaleFactor),
        nameof(ConvertViewModel.BuildMaterials),
        nameof(ConvertViewModel.GeneratePhysics),
        nameof(ConvertViewModel.PhysicsMass),
        nameof(ConvertViewModel.CoacdThreshold),
        nameof(ConvertViewModel.MaxConvexPieces),
        nameof(ConvertViewModel.MaxHullVertices)
    };

    private static readonly HashSet<string> _explorerSettingsProperties = new(StringComparer.Ordinal)
    {
        nameof(ExplorerViewModel.SelectedExplorerProfile),
        nameof(ExplorerViewModel.ExplorerRootDirectory),
        nameof(ExplorerViewModel.ExplorerFilter)
    };

    private static readonly HashSet<string> _shellSettingsProperties = new(StringComparer.Ordinal)
    {
        nameof(PreviewViewModel.PreviewOrthographic),
        nameof(PreviewViewModel.PreviewWireframe),
        nameof(PreviewViewModel.PreviewPhysicsOverlay)
    };

    private readonly UiLogSink _logSink = new();
    private readonly ConversionService _conversionService;
    private readonly ExplorerService _explorerService = new();
    private CancellationTokenSource? _settingsSaveCts;
    private bool _disposed;
    private readonly bool _suppressSettingsSave;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _selectedWorkspaceIndex;

    [ObservableProperty]
    private bool _isSidebarExpanded = true;

    [ObservableProperty]
    private bool _isPreviewPaneExpanded = true;

    [ObservableProperty]
    private double _previewPaneRatio = 0.25;

    public MainWindowViewModel()
    {
        _suppressSettingsSave = true;
        _conversionService = new ConversionService(_logSink);
        Preview = new PreviewViewModel(_logSink);
        Console = new ConsoleViewModel(_logSink);
        Convert = new ConvertViewModel(
            _logSink,
            _conversionService,
            () => IsBusy,
            value => IsBusy = value,
            value => Preview.StatusMessage = value,
            Preview.ApplyResult);
        Explorer = new ExplorerViewModel(
            _logSink,
            _explorerService,
            _conversionService,
            Convert,
            () => IsBusy,
            value => IsBusy = value,
            value => Preview.StatusMessage = value);

        Convert.TryLoadDefaultConfig();
        TryLoadSettings();
        _suppressSettingsSave = false;

        Convert.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null && _convertSettingsProperties.Contains(e.PropertyName))
            {
                QueueSettingsSave();
            }
        };
        Explorer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null && _explorerSettingsProperties.Contains(e.PropertyName))
            {
                QueueSettingsSave();
            }
        };
        Preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null && _shellSettingsProperties.Contains(e.PropertyName))
            {
                QueueSettingsSave();
            }
        };
    }

    public ConvertViewModel Convert { get; }

    public ExplorerViewModel Explorer { get; }

    public PreviewViewModel Preview { get; }

    public ConsoleViewModel Console { get; }

    public bool IsConvertWorkspace => SelectedWorkspaceIndex == 0;

    public bool IsExplorerWorkspace => SelectedWorkspaceIndex == 1;

    public bool IsConsoleWorkspace => SelectedWorkspaceIndex == 2;

    public bool IsSettingsWorkspace => SelectedWorkspaceIndex == 3;

    public string CurrentRoute => SelectedWorkspaceIndex switch
    {
        1 => "explorer",
        2 => "console",
        3 => "settings",
        _ => "convert"
    };

    public bool IsSidebarCollapsed => !IsSidebarExpanded;

    public bool IsPreviewPaneCollapsed => !IsPreviewPaneExpanded;

    partial void OnIsBusyChanged(bool value)
    {
        Convert.NotifyBusyChanged();
        Explorer.NotifyBusyChanged();
    }

    partial void OnSelectedWorkspaceIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsConvertWorkspace));
        OnPropertyChanged(nameof(IsExplorerWorkspace));
        OnPropertyChanged(nameof(IsConsoleWorkspace));
        OnPropertyChanged(nameof(IsSettingsWorkspace));
        OnPropertyChanged(nameof(CurrentRoute));
    }

    partial void OnIsSidebarExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSidebarCollapsed));
    }

    partial void OnIsPreviewPaneExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPreviewPaneCollapsed));
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
    }

    [RelayCommand]
    private void TogglePreviewPane()
    {
        IsPreviewPaneExpanded = !IsPreviewPaneExpanded;
    }

    public void AdjustPreviewPaneRatio(double horizontalChange, double flexibleWidth)
    {
        if (flexibleWidth <= 0)
        {
            return;
        }

        PreviewPaneRatio = Math.Clamp(
            PreviewPaneRatio - horizontalChange / flexibleWidth,
            _minPreviewPaneRatio,
            _maxPreviewPaneRatio);
    }

    [RelayCommand]
    private void ShowConvert()
    {
        SelectedWorkspaceIndex = 0;
    }

    [RelayCommand]
    private void ShowExplorer()
    {
        SelectedWorkspaceIndex = 1;
    }

    [RelayCommand]
    private void ShowConsole()
    {
        SelectedWorkspaceIndex = 2;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        SelectedWorkspaceIndex = 3;
    }

    private void TryLoadSettings()
    {
        try
        {
            if (UiSettings.Load() is { } settings)
            {
                ApplySettings(settings);
                _logSink.Append($"Loaded UI settings: {UiSettings.SettingsPath}");
            }
        }
        catch (GMConverterException ex)
        {
            _logSink.Append(ex.Message);
        }
    }

    private void ApplySettings(UiSettings settings)
    {
        Convert.ApplySettings(settings);
        Explorer.ApplySettings(settings);
        Preview.ApplySettings(settings);
    }

    private UiSettings CaptureSettingsState()
    {
        return new UiSettings(
            Convert.SelectedInputFormat.Value,
            Convert.SelectedOutputFormat.Value,
            Convert.SelectedAxisMode.Value,
            Explorer.SelectedExplorerProfile.Value,
            Convert.SelectedPhysicsMode.Value,
            EmptyToNull(Convert.ConfigPath),
            EmptyToNull(Convert.InputPath),
            EmptyToNull(Convert.AnimationPath),
            EmptyToNull(Convert.OutputPath),
            EmptyToNull(Convert.BaseName),
            EmptyToNull(Convert.ModelPath),
            EmptyToNull(Convert.StudioMdlPath),
            EmptyToNull(Convert.VtfCmdPath),
            EmptyToNull(Convert.MaterialDirectory),
            EmptyToNull(Explorer.ExplorerRootDirectory),
            EmptyToNull(Explorer.ExplorerFilter),
            Convert.ScaleFactor,
            Convert.BuildMaterials,
            Convert.GeneratePhysics,
            Preview.PreviewOrthographic,
            Preview.PreviewWireframe,
            Preview.PreviewPhysicsOverlay,
            Convert.PhysicsMass,
            Convert.CoacdThreshold,
            Convert.MaxConvexPieces,
            Convert.MaxHullVertices);
    }

    private void QueueSettingsSave()
    {
        if (_suppressSettingsSave || _disposed)
        {
            return;
        }

        _settingsSaveCts?.Cancel();
        _settingsSaveCts = new CancellationTokenSource();
        var token = _settingsSaveCts.Token;
        _ = SaveSettingsSoonAsync(token);
    }

    private async Task SaveSettingsSoonAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(500, cancellationToken);
            SaveSettingsNow();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException)
        {
            _logSink.Append($"Failed to save UI settings. {ex.Message}");
        }
    }

    public void SaveSettingsNow()
    {
        if (_suppressSettingsSave || _disposed)
        {
            return;
        }

        try
        {
            _settingsSaveCts?.Cancel();
            CaptureSettingsState().Save();
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException)
        {
            _logSink.Append($"Failed to save UI settings. {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settingsSaveCts?.Cancel();
        _settingsSaveCts?.Dispose();
        _settingsSaveCts = null;
        Explorer.Dispose();
        Console.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
