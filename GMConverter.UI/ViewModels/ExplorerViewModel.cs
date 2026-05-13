using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GMConverter.Common;
using GMConverter.Explorer;
using GMConverter.UI.Models;
using GMConverter.UI.Services;

namespace GMConverter.UI.ViewModels;

public sealed partial class ExplorerViewModel : ViewModelBase, IDisposable
{
    private readonly UiLogSink _logSink;
    private readonly ExplorerService _explorerService;
    private readonly ConversionService _conversionService;
    private readonly ConvertViewModel _convert;
    private readonly Func<bool> _getIsBusy;
    private readonly Action<bool> _setIsBusy;
    private readonly Action<string> _setStatusMessage;
    private readonly List<ExplorerFileEntry> _explorerEntries = [];
    private CancellationTokenSource? _explorerFilterCts;
    private string _explorerProfileName = "Explorer";
    private bool _disposed;

    [ObservableProperty]
    private DisplayOption _selectedExplorerProfile;

    [ObservableProperty]
    private ExplorerNode? _selectedExplorerNode;

    [ObservableProperty]
    private string _explorerRootDirectory = string.Empty;

    [ObservableProperty]
    private string _explorerFilter = string.Empty;

    [ObservableProperty]
    private string _selectedExplorerDetails = "Select a model in the tree.";

    [ObservableProperty]
    private string _explorerStatus = "Select a root directory and scan for supported models.";

    internal ExplorerViewModel(
        UiLogSink logSink,
        ExplorerService explorerService,
        ConversionService conversionService,
        ConvertViewModel convert,
        Func<bool> getIsBusy,
        Action<bool> setIsBusy,
        Action<string> setStatusMessage)
    {
        _logSink = logSink;
        _explorerService = explorerService;
        _conversionService = conversionService;
        _convert = convert;
        _getIsBusy = getIsBusy;
        _setIsBusy = setIsBusy;
        _setStatusMessage = setStatusMessage;

        foreach (var profile in _explorerService.Profiles)
        {
            ExplorerProfiles.Add(new DisplayOption(profile.Id, profile.DisplayName, string.Empty));
        }

        _selectedExplorerProfile = ExplorerProfiles[0];
    }

    public ObservableCollection<DisplayOption> ExplorerProfiles { get; } =
    [
        new(ExplorerService.AutoProfileId, "Auto-detect", string.Empty)
    ];

    public ObservableCollection<ExplorerNode> ExplorerNodes { get; } = [];

    public bool HasSelectedExplorerEntry => SelectedExplorerNode?.Entry is not null;

    public bool HasExplorerFilter => !string.IsNullOrWhiteSpace(ExplorerFilter);

    public bool IsIdle => !_getIsBusy();

    partial void OnSelectedExplorerNodeChanged(ExplorerNode? value)
    {
        OnPropertyChanged(nameof(HasSelectedExplorerEntry));
        PreviewExplorerSelectionCommand.NotifyCanExecuteChanged();
        ExportExplorerSelectionCommand.NotifyCanExecuteChanged();

        if (value?.Entry is { } entry)
        {
            PopulateExplorerSelection(entry);
        }
        else
        {
            SelectedExplorerDetails = "Select a model in the tree.";
        }
    }

    partial void OnExplorerFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasExplorerFilter));
        ClearExplorerFilterCommand.NotifyCanExecuteChanged();
        QueueExplorerFilterRebuild();
    }

    internal void NotifyBusyChanged()
    {
        OnPropertyChanged(nameof(IsIdle));
        ScanExplorerCommand.NotifyCanExecuteChanged();
        RefreshExplorerCommand.NotifyCanExecuteChanged();
        PreviewExplorerSelectionCommand.NotifyCanExecuteChanged();
        ExportExplorerSelectionCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task ScanExplorerAsync()
    {
        await ScanExplorerAsync(clearCaches: false);
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task RefreshExplorerAsync()
    {
        await ScanExplorerAsync(clearCaches: true);
    }

    [RelayCommand(CanExecute = nameof(CanUseExplorerSelection))]
    private async Task PreviewExplorerSelectionAsync()
    {
        if (_getIsBusy() ||
            SelectedExplorerNode?.Entry is not { } entry)
        {
            return;
        }

        _setIsBusy(true);
        try
        {
            await ApplyExplorerSelectionAsync(entry);
            await _convert.LoadPreviewCoreAsync();
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _setStatusMessage("Explorer preview failed.");
            ExplorerStatus = "Preview failed.";
            _logSink.Append($"Explorer preview failed. {ex.Message}");
        }
        finally
        {
            _setIsBusy(false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseExplorerSelection))]
    private async Task ExportExplorerSelectionAsync()
    {
        if (_getIsBusy() ||
            SelectedExplorerNode?.Entry is not { } entry)
        {
            return;
        }

        _setIsBusy(true);
        try
        {
            await ApplyExplorerSelectionAsync(entry);
            _setStatusMessage("Running conversion...");
            _logSink.Append("Running conversion...");
            var result = await Task.Run(() => _conversionService.RunConversion(_convert.CaptureSettings()));
            _logSink.Append(result);
            _setStatusMessage("Done.");
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _setStatusMessage("Explorer export failed.");
            ExplorerStatus = "Export failed.";
            _logSink.Append($"Explorer export failed. {ex.Message}");
        }
        finally
        {
            _setIsBusy(false);
        }
    }

    [RelayCommand(CanExecute = nameof(HasExplorerFilter))]
    private void ClearExplorerFilter()
    {
        ExplorerFilter = string.Empty;
        _explorerFilterCts?.Cancel();
        RebuildExplorerNodes();
    }

    internal void ApplySettings(UiSettings settings)
    {
        ConvertViewModel.SetSelected(ExplorerProfiles, settings.ExplorerProfile, value => SelectedExplorerProfile = value);
        ExplorerRootDirectory = settings.ExplorerRootDirectory ?? ExplorerRootDirectory;
        ExplorerFilter = settings.ExplorerFilter ?? ExplorerFilter;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _explorerFilterCts?.Cancel();
        _explorerFilterCts?.Dispose();
        _explorerFilterCts = null;
    }

    private async Task ScanExplorerAsync(bool clearCaches)
    {
        if (_getIsBusy())
        {
            return;
        }

        _setIsBusy(true);
        var message = clearCaches ? "Refreshing explorer..." : "Scanning explorer root...";
        _setStatusMessage(message);
        ExplorerStatus = message;
        _logSink.Append(message);
        try
        {
            var result = await Task.Run(() =>
            {
                if (clearCaches)
                {
                    _explorerService.ClearCaches();
                }

                return _explorerService.Scan(ExplorerRootDirectory, SelectedExplorerProfile.Value);
            });
            _explorerEntries.Clear();
            _explorerEntries.AddRange(result.Entries);
            _explorerProfileName = result.Profile.DisplayName;
            RebuildExplorerNodes();
            _setStatusMessage(clearCaches ? "Explorer refresh complete." : "Explorer scan complete.");
            _logSink.Append($"Explorer found {result.Entries.Count} supported model file(s) using {result.Profile.DisplayName}.");
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ExplorerStatus = "Scan failed.";
            _setStatusMessage("Explorer scan failed.");
            _logSink.Append(ex.Message);
        }
        finally
        {
            _setIsBusy(false);
        }
    }

    private bool CanUseExplorerSelection()
    {
        return HasSelectedExplorerEntry && !_getIsBusy();
    }

    private bool CanRunCommand()
    {
        return IsIdle;
    }

    private async Task ApplyExplorerSelectionAsync(ExplorerFileEntry entry)
    {
        var resolveMessage = entry.ArchivePath is null
            ? "Preparing explorer selection..."
            : "Resolving archive model and textures...";
        _setStatusMessage(resolveMessage);
        ExplorerStatus = resolveMessage;
        _logSink.Append(resolveMessage);

        var resolvedEntry = await Task.Run(() => _explorerService.ResolveEntry(entry));
        PopulateExplorerSelection(entry, resolvedEntry);
        ExplorerStatus = $"Prepared {entry.DisplayPath}.";
    }

    private void PopulateExplorerSelection(ExplorerFileEntry fileEntry, ExplorerResolvedEntry? resolvedEntry = null)
    {
        _convert.ApplyExplorerSelection(fileEntry, resolvedEntry);
        SelectedExplorerDetails = FormatExplorerDetails(fileEntry);
    }

    private void AddExplorerEntry(ExplorerFileEntry fileEntry)
    {
        var nodes = ExplorerNodes;
        ExplorerNode? current = null;

        var segments = fileEntry.DisplayPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var expandNodes = HasExplorerFilter;

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var node = nodes.FirstOrDefault(item => string.Equals(item.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (node is null)
            {
                node = new ExplorerNode(segment, GetExplorerNodeKind(segment, i == segments.Length - 1));
                nodes.Add(node);
            }
            else if (IsArchiveSegment(segment))
            {
                node.MarkAsArchive();
            }

            if (expandNodes && i < segments.Length - 1)
            {
                node.IsExpanded = true;
            }

            current = node;
            nodes = node.Children;
        }

        current?.MarkAsModel(fileEntry);
    }

    private void RebuildExplorerNodes()
    {
        _explorerFilterCts?.Cancel();
        ExplorerNodes.Clear();
        SelectedExplorerNode = null;

        var filteredEntries = FilterExplorerEntries().ToArray();
        foreach (var entry in filteredEntries)
        {
            AddExplorerEntry(entry);
        }

        ExplorerStatus = string.IsNullOrWhiteSpace(ExplorerFilter)
            ? $"{_explorerProfileName}: {_explorerEntries.Count} supported model file(s)."
            : $"{_explorerProfileName}: {filteredEntries.Length} of {_explorerEntries.Count} supported model file(s).";
    }

    private IEnumerable<ExplorerFileEntry> FilterExplorerEntries()
    {
        var terms = ExplorerFilter
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return _explorerEntries;
        }

        return _explorerEntries.Where(entry => terms.All(term =>
            entry.DisplayPath.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private void QueueExplorerFilterRebuild()
    {
        _explorerFilterCts?.Cancel();

        if (_explorerEntries.Count == 0)
        {
            return;
        }

        _explorerFilterCts = new CancellationTokenSource();
        var token = _explorerFilterCts.Token;
        _ = RebuildExplorerNodesSoonAsync(token);
    }

    private async Task RebuildExplorerNodesSoonAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    RebuildExplorerNodes();
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string FormatExplorerDetails(ExplorerFileEntry fileEntry)
    {
        var location = fileEntry.ArchivePath is null
            ? fileEntry.FilePath
            : $"{fileEntry.ArchivePath} [{fileEntry.ArchiveEntryPath}]";
        return $"{fileEntry.InputFormat.ToUpperInvariant()} | {location}";
    }

    private static ExplorerNodeKind GetExplorerNodeKind(string segment, bool isLastSegment)
    {
        if (isLastSegment)
        {
            return ExplorerNodeKind.Model;
        }

        return IsArchiveSegment(segment)
            ? ExplorerNodeKind.Archive
            : ExplorerNodeKind.Folder;
    }

    private static bool IsArchiveSegment(string segment)
    {
        return string.Equals(Path.GetExtension(segment), ".pak", StringComparison.OrdinalIgnoreCase);
    }
}
