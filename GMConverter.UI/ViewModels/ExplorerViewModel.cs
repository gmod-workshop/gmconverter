using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GMConverter.Explorer;
using GMConverter.UI.Models;
using GMConverter.UI.Services;

namespace GMConverter.UI.ViewModels;

public sealed partial class ExplorerViewModel : ViewModelBase, IDisposable
{
    private const int _maxDisplayedExplorerEntries = 2500;

    private static readonly char[] _searchTokenSeparators =
    [
        '/',
        '\\',
        '.',
        '_',
        '-',
        ' ',
        ':',
        '|',
        '(',
        ')',
        '[',
        ']'
    ];

    private static readonly HashSet<string> _relatedAnimationStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "anim",
        "animation",
        "animations",
        "assets",
        "blueprint",
        "bp",
        "class",
        "comp",
        "content",
        "fortnite",
        "fortplaysetitemdefinition",
        "fortplaysetpropitemdefinition",
        "game",
        "map",
        "maps",
        "object",
        "pid",
        "ppid",
        "ppids",
        "props",
        "registry",
        "setupassets",
        "skeletalmesh",
        "sk",
        "sm",
        "staticmesh",
        "uasset"
    };

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

    public bool HasConvertibleExplorerEntry => SelectedExplorerNode?.Entry?.IsConvertible is true;

    public bool HasAnimationExplorerEntry =>
        SelectedExplorerNode?.Entry is { } entry &&
        entry.InputFormat.Equals("ueanim", StringComparison.OrdinalIgnoreCase) &&
        IsExportableAnimationEntryClass(entry.AssetClass);

    public bool HasExplorerFilter => !string.IsNullOrWhiteSpace(ExplorerFilter);

    public bool HasExplorerEntries => _explorerEntries.Count > 0;

    public bool CanFindRelatedExplorerAnimations => HasSelectedExplorerEntry && HasAnimationExplorerEntries();

    public bool IsIdle => !_getIsBusy();

    partial void OnSelectedExplorerNodeChanged(ExplorerNode? value)
    {
        OnPropertyChanged(nameof(HasSelectedExplorerEntry));
        OnPropertyChanged(nameof(HasConvertibleExplorerEntry));
        OnPropertyChanged(nameof(HasAnimationExplorerEntry));
        OnPropertyChanged(nameof(CanFindRelatedExplorerAnimations));
        FindRelatedExplorerAnimationsCommand.NotifyCanExecuteChanged();
        PreviewExplorerSelectionCommand.NotifyCanExecuteChanged();
        ExportExplorerSelectionCommand.NotifyCanExecuteChanged();
        UseSelectionAsAnimationCommand.NotifyCanExecuteChanged();

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
        UseSelectionAsAnimationCommand.NotifyCanExecuteChanged();
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
        catch (Exception ex)
        {
            _setStatusMessage("Explorer preview failed.");
            ExplorerStatus = "Preview failed.";
            _logSink.Append($"Explorer preview failed. {ex}");
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
        catch (Exception ex)
        {
            _setStatusMessage("Explorer export failed.");
            ExplorerStatus = "Export failed.";
            _logSink.Append($"Explorer export failed. {ex}");
        }
        finally
        {
            _setIsBusy(false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectionAsAnimation))]
    private async Task UseSelectionAsAnimationAsync()
    {
        if (_getIsBusy() ||
            SelectedExplorerNode?.Entry is not { } entry)
        {
            return;
        }

        _setIsBusy(true);
        try
        {
            _setStatusMessage("Exporting animation...");
            ExplorerStatus = "Exporting animation...";
            _logSink.Append("Exporting animation...");

            var resolvedEntry = await Task.Run(() => _explorerService.ResolveEntry(entry));
            var animPath = resolvedEntry.AnimationPath ?? resolvedEntry.InputPath;
            _convert.AnimationPath = animPath;

            if (!string.IsNullOrWhiteSpace(resolvedEntry.Details))
            {
                _logSink.Append(resolvedEntry.Details);
            }

            ExplorerStatus = $"Set animation: {Path.GetFileName(animPath)}.";
            _logSink.Append($"Set animation: {animPath}");
            _setStatusMessage("Animation set.");
        }
        catch (Exception ex)
        {
            _setStatusMessage("Animation export failed.");
            ExplorerStatus = "Animation export failed.";
            _logSink.Append($"Animation export failed. {ex.Message}");
        }
        finally
        {
            _setIsBusy(false);
        }
    }

    private bool CanUseSelectionAsAnimation()
    {
        return HasAnimationExplorerEntry && !_getIsBusy();
    }

    private static bool IsExportableAnimationEntryClass(string? assetClass)
    {
        return assetClass is not null &&
            (assetClass.Equals("AnimSequence", StringComparison.OrdinalIgnoreCase) ||
             assetClass.Equals("AnimMontage", StringComparison.OrdinalIgnoreCase) ||
             assetClass.Equals("AnimComposite", StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand(CanExecute = nameof(HasExplorerFilter))]
    private void ClearExplorerFilter()
    {
        ExplorerFilter = string.Empty;
        _explorerFilterCts?.Cancel();
        RebuildExplorerNodes();
    }

    [RelayCommand(CanExecute = nameof(HasExplorerEntries))]
    private void ShowExplorerAnimations()
    {
        ApplyExplorerFilter("class:Anim");
    }

    [RelayCommand(CanExecute = nameof(CanFindRelatedExplorerAnimations))]
    private void FindRelatedExplorerAnimations()
    {
        if (SelectedExplorerNode?.Entry is not { } entry)
        {
            return;
        }

        var tokens = CreateRelatedAnimationTokens(entry);
        var filter = tokens.Length == 0
            ? "class:Anim"
            : $"class:Anim any:{string.Join(',', tokens)}";
        ApplyExplorerFilter(filter);
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
            OnPropertyChanged(nameof(HasExplorerEntries));
            OnPropertyChanged(nameof(CanFindRelatedExplorerAnimations));
            ShowExplorerAnimationsCommand.NotifyCanExecuteChanged();
            FindRelatedExplorerAnimationsCommand.NotifyCanExecuteChanged();
            RebuildExplorerNodes();
            _setStatusMessage(clearCaches ? "Explorer refresh complete." : "Explorer scan complete.");
            _logSink.Append($"Explorer found {result.Entries.Count} supported asset(s) using {result.Profile.DisplayName}.");
        }
        catch (Exception ex)
        {
            ExplorerStatus = "Scan failed.";
            _setStatusMessage("Explorer scan failed.");
            _logSink.Append($"Explorer scan failed. {ex.Message}");
        }
        finally
        {
            _setIsBusy(false);
        }
    }

    private bool CanUseExplorerSelection()
    {
        return HasConvertibleExplorerEntry && !_getIsBusy();
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
        if (!string.IsNullOrWhiteSpace(resolvedEntry.Details))
        {
            _logSink.Append(resolvedEntry.Details);
        }

        if (!string.IsNullOrWhiteSpace(resolvedEntry.AnimationPath))
        {
            _logSink.Append($"Resolved animation sidecar: {resolvedEntry.AnimationPath}");
        }

        ExplorerStatus = string.IsNullOrWhiteSpace(resolvedEntry.AnimationPath)
            ? $"Prepared {entry.DisplayPath}."
            : $"Prepared {entry.DisplayPath} with animation.";
    }

    private void PopulateExplorerSelection(ExplorerFileEntry fileEntry, ExplorerResolvedEntry? resolvedEntry = null)
    {
        if (fileEntry.IsConvertible)
        {
            _convert.ApplyExplorerSelection(fileEntry, resolvedEntry);
        }

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

        var filteredEntries = BuildFilteredExplorerEntries();
        foreach (var entry in filteredEntries.Entries)
        {
            AddExplorerEntry(entry);
        }

        ExplorerStatus = string.IsNullOrWhiteSpace(ExplorerFilter)
            ? FormatUnfilteredExplorerStatus(filteredEntries)
            : FormatFilteredExplorerStatus(filteredEntries);
    }

    private FilteredExplorerEntries BuildFilteredExplorerEntries()
    {
        var terms = ExplorerFilter
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<ExplorerFileEntry> entries = [];
        var matchCount = 0;

        foreach (var entry in _explorerEntries)
        {
            if (terms.Length > 0 && !terms.All(term => MatchesExplorerFilterTerm(entry, term)))
            {
                continue;
            }

            matchCount++;
            if (entries.Count < _maxDisplayedExplorerEntries)
            {
                entries.Add(entry);
            }
        }

        return new FilteredExplorerEntries(entries, matchCount, matchCount > entries.Count);
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

    private void ApplyExplorerFilter(string filter)
    {
        ExplorerFilter = filter;
        _explorerFilterCts?.Cancel();
        RebuildExplorerNodes();
    }

    private static string FormatExplorerDetails(ExplorerFileEntry fileEntry)
    {
        var location = fileEntry.ArchivePath is null
            ? fileEntry.FilePath
            : $"{fileEntry.ArchivePath} [{fileEntry.ArchiveEntryPath}]";
        var conversionStatus = fileEntry.IsConvertible ? string.Empty : " | Browse only";
        var details = string.IsNullOrWhiteSpace(fileEntry.Details) ? string.Empty : $" | {fileEntry.Details}";
        return $"{fileEntry.InputFormat.ToUpperInvariant()}{conversionStatus} | {location}{details}";
    }

    private static string FormatExplorerEntryCount(IEnumerable<ExplorerFileEntry> entries)
    {
        var entriesArray = entries as ExplorerFileEntry[] ?? [.. entries];
        var animationCount = entriesArray.Count(IsAnimationEntry);
        var modelCount = entriesArray.Length - animationCount;
        return animationCount == 0
            ? $"{entriesArray.Length} supported model file(s)"
            : $"{modelCount} supported model file(s), {animationCount} animation asset(s)";
    }

    private static string FormatFilteredExplorerStatus(FilteredExplorerEntries filteredEntries)
    {
        return filteredEntries.IsTruncated
            ? $"{filteredEntries.Entries.Count:N0} of {filteredEntries.MatchCount:N0} matching asset(s) displayed. Narrow the filter to show fewer results."
            : $"{filteredEntries.Entries.Count:N0} matching asset(s).";
    }

    private string FormatUnfilteredExplorerStatus(FilteredExplorerEntries filteredEntries)
    {
        var countDetails = $"{_explorerProfileName}: {FormatExplorerEntryCount(_explorerEntries)}.";
        return filteredEntries.IsTruncated
            ? $"{countDetails} Showing first {filteredEntries.Entries.Count:N0}; use the filter to narrow results."
            : countDetails;
    }

    private static bool MatchesExplorerFilterTerm(ExplorerFileEntry entry, string term)
    {
        if (TryMatchPrefixedFilter(entry, term, "class:", value => entry.AssetClass?.Contains(value, StringComparison.OrdinalIgnoreCase) is true))
        {
            return true;
        }

        if (TryMatchPrefixedFilter(entry, term, "format:", value => entry.InputFormat.Contains(value, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (TryMatchPrefixedFilter(entry, term, "type:", value => entry.InputFormat.Contains(value, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (term.StartsWith("any:", StringComparison.OrdinalIgnoreCase))
        {
            var values = term["any:".Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return values.Any(value => ContainsExplorerSearchText(entry, value));
        }

        return ContainsExplorerSearchText(entry, term);
    }

    private static bool TryMatchPrefixedFilter(ExplorerFileEntry entry, string term, string prefix, Func<string, bool> matcher)
    {
        return term.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            matcher(term[prefix.Length..]);
    }

    private static bool ContainsExplorerSearchText(ExplorerFileEntry entry, string value)
    {
        return entry.DisplayPath.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            entry.ArchiveEntryPath?.Contains(value, StringComparison.OrdinalIgnoreCase) is true ||
            entry.AssetClass?.Contains(value, StringComparison.OrdinalIgnoreCase) is true ||
            entry.Details?.Contains(value, StringComparison.OrdinalIgnoreCase) is true ||
            entry.InputFormat.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] CreateRelatedAnimationTokens(ExplorerFileEntry entry)
    {
        return
        [
            .. EnumerateRelatedAnimationTokens(entry)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(token => token.Length)
            .ThenBy(token => token, StringComparer.OrdinalIgnoreCase)
            .Take(10)
        ];
    }

    private static IEnumerable<string> EnumerateRelatedAnimationTokens(ExplorerFileEntry entry)
    {
        var text = $"{entry.DisplayPath} {entry.ArchiveEntryPath} {entry.FilePath}";
        foreach (var token in text.Split(_searchTokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var normalizedToken in NormalizeRelatedAnimationToken(token))
            {
                yield return normalizedToken;
            }
        }
    }

    private static IEnumerable<string> NormalizeRelatedAnimationToken(string token)
    {
        var trimmed = token.Trim();
        if (IsUsefulRelatedAnimationToken(trimmed))
        {
            yield return trimmed;
        }

        foreach (var segment in SplitCamelCaseToken(trimmed))
        {
            if (IsUsefulRelatedAnimationToken(segment))
            {
                yield return segment;
            }
        }
    }

    private static IEnumerable<string> SplitCamelCaseToken(string token)
    {
        var start = 0;
        for (var i = 1; i < token.Length; i++)
        {
            if (char.IsUpper(token[i]) && (char.IsLower(token[i - 1]) || i + 1 < token.Length && char.IsLower(token[i + 1])))
            {
                yield return token[start..i];
                start = i;
            }
        }

        yield return token[start..];
    }

    private static bool IsUsefulRelatedAnimationToken(string token)
    {
        return token.Length >= 3 &&
            !_relatedAnimationStopWords.Contains(token) &&
            !token.All(Uri.IsHexDigit);
    }

    private bool HasAnimationExplorerEntries()
    {
        return _explorerEntries.Any(IsAnimationEntry);
    }

    private static bool IsAnimationEntry(ExplorerFileEntry entry)
    {
        return entry.InputFormat.Equals("ueanim", StringComparison.OrdinalIgnoreCase) ||
            entry.AssetClass?.Equals("AnimSequence", StringComparison.OrdinalIgnoreCase) is true ||
            entry.AssetClass?.Equals("AnimMontage", StringComparison.OrdinalIgnoreCase) is true;
    }

    private sealed record FilteredExplorerEntries(
        IReadOnlyList<ExplorerFileEntry> Entries,
        int MatchCount,
        bool IsTruncated);

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
        return Path.GetExtension(segment).ToLowerInvariant() is ".pak" or ".utoc" or ".ukx" or ".usx" or ".utx" or ".uax" or ".u" or ".umx" or ".unr" or ".ctm" or ".upx";
    }

}
