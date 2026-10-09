using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.UI.Services;

namespace GMConverter.UI.ViewModels;

public sealed partial class PreviewViewModel : ViewModelBase
{
    private readonly UiLogSink _logSink;

    [ObservableProperty]
    private bool _previewOrthographic;

    [ObservableProperty]
    private bool _previewWireframe;

    [ObservableProperty]
    private bool _previewPhysicsOverlay;

    [ObservableProperty]
    private string _previewFileLabel = "No model";

    [ObservableProperty]
    private string _previewModelPath = string.Empty;

    [ObservableProperty]
    private string _previewPhysicsModelPath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    internal PreviewViewModel(UiLogSink logSink)
    {
        _logSink = logSink;
    }

    public ObservableCollection<string> LogLines => _logSink.Lines;

    public bool IsPreviewEmpty => string.IsNullOrWhiteSpace(PreviewModelPath);

    public bool HasPreviewPhysics => !string.IsNullOrWhiteSpace(PreviewPhysicsModelPath);

    partial void OnPreviewModelPathChanged(string value)
    {
        OnPropertyChanged(nameof(IsPreviewEmpty));
    }

    partial void OnPreviewPhysicsModelPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasPreviewPhysics));
    }

    internal void ApplySettings(UiSettings settings)
    {
        PreviewOrthographic = settings.PreviewOrthographic;
        PreviewWireframe = settings.PreviewWireframe;
        PreviewPhysicsOverlay = settings.PreviewPhysicsOverlay;
    }

    internal void ApplyResult(PreviewLoadResult result, string inputPath)
    {
        PreviewPhysicsModelPath = string.Empty;
        UpdatePreviewFileLabel(result, inputPath);
        PreviewPhysicsModelPath = result.PhysicsModelPath ?? string.Empty;
        PreviewModelPath = result.ModelPath;
    }

    internal void AppendLog(string? message)
    {
        _logSink.Append(message);
    }

    private void UpdatePreviewFileLabel(PreviewLoadResult result, string inputPath)
    {
        PreviewFileLabel = string.IsNullOrWhiteSpace(inputPath)
            ? Path.GetFileName(result.ModelPath)
            : Path.GetFileName(inputPath);
    }
}
