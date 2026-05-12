using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GMConverter.Common;
using GMConverter.Explorer;
using GMConverter.Exporters;
using GMConverter.Importers;
using GMConverter.UI.Models;
using GMConverter.UI.Services;

namespace GMConverter.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const double _defaultCoacdThreshold = 0.05;
    private const int _defaultMaxConvexPieces = 16;
    private const int _defaultMaxHullVertices = 16;
    private const double _legacyDefaultCoacdThreshold = 0.01;
    private const int _legacyDefaultMaxConvexPieces = 32;
    private const int _legacyDefaultMaxHullVertices = 32;

    private static readonly HashSet<string> _persistedPropertyNames = new(StringComparer.Ordinal)
    {
        nameof(SelectedInputFormat),
        nameof(SelectedOutputFormat),
        nameof(SelectedAxisMode),
        nameof(SelectedExplorerProfile),
        nameof(SelectedPhysicsMode),
        nameof(ConfigPath),
        nameof(InputPath),
        nameof(AnimationPath),
        nameof(OutputPath),
        nameof(BaseName),
        nameof(ModelPath),
        nameof(GameDirectory),
        nameof(EngineDirectory),
        nameof(MaterialDirectory),
        nameof(ExplorerRootDirectory),
        nameof(ExplorerFilter),
        nameof(ScaleFactor),
        nameof(BuildMaterials),
        nameof(GeneratePhysics),
        nameof(PreviewOrthographic),
        nameof(PreviewWireframe),
        nameof(PreviewPhysicsOverlay),
        nameof(PhysicsMass),
        nameof(CoacdThreshold),
        nameof(MaxConvexPieces),
        nameof(MaxHullVertices)
    };

    private readonly UiLogSink _logSink = new();
    private readonly ConversionService _conversionService;
    private readonly ExplorerService _explorerService = new();
    private readonly List<ExplorerFileEntry> _explorerEntries = [];
    private CancellationTokenSource? _settingsSaveCts;
    private CancellationTokenSource? _explorerFilterCts;
    private bool _disposed;
    private bool _suppressSettingsSave;
    private string _explorerProfileName = "Explorer";

    [ObservableProperty]
    private DisplayOption _selectedInputFormat;

    [ObservableProperty]
    private DisplayOption _selectedOutputFormat;

    [ObservableProperty]
    private DisplayOption _selectedAxisMode;

    [ObservableProperty]
    private DisplayOption _selectedExplorerProfile;

    [ObservableProperty]
    private ExplorerNode? _selectedExplorerNode;

    [ObservableProperty]
    private DisplayOption _selectedPhysicsMode;

    [ObservableProperty]
    private string _configPath = string.Empty;

    [ObservableProperty]
    private string _inputPath = string.Empty;

    [ObservableProperty]
    private string _animationPath = string.Empty;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private string _baseName = string.Empty;

    [ObservableProperty]
    private string _modelPath = "gmconverter/model.mdl";

    [ObservableProperty]
    private string _gameDirectory = string.Empty;

    [ObservableProperty]
    private string _engineDirectory = string.Empty;

    [ObservableProperty]
    private string _materialDirectory = string.Empty;

    [ObservableProperty]
    private string _explorerRootDirectory = string.Empty;

    [ObservableProperty]
    private string _explorerFilter = string.Empty;

    [ObservableProperty]
    private string _selectedExplorerDetails = "Select a model in the tree.";

    [ObservableProperty]
    private double _scaleFactor = 1.0;

    [ObservableProperty]
    private bool _buildMaterials = true;

    [ObservableProperty]
    private bool _generatePhysics;

    [ObservableProperty]
    private bool _previewOrthographic;

    [ObservableProperty]
    private bool _previewWireframe;

    [ObservableProperty]
    private bool _previewPhysicsOverlay;

    [ObservableProperty]
    private double _physicsMass = 100.0;

    [ObservableProperty]
    private double _coacdThreshold = _defaultCoacdThreshold;

    [ObservableProperty]
    private int _maxConvexPieces = _defaultMaxConvexPieces;

    [ObservableProperty]
    private int _maxHullVertices = _defaultMaxHullVertices;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _previewText = "No preview loaded.";

    [ObservableProperty]
    private string _previewModelPath = string.Empty;

    [ObservableProperty]
    private string _previewPhysicsModelPath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private string _explorerStatus = "Select a root directory and scan for supported models.";

    public MainWindowViewModel()
    {
        _suppressSettingsSave = true;
        _conversionService = new ConversionService(_logSink);
        foreach (var profile in _explorerService.Profiles)
        {
            ExplorerProfiles.Add(new DisplayOption(profile.Id, profile.DisplayName, string.Empty));
        }

        _selectedInputFormat = InputFormats[0];
        _selectedOutputFormat = OutputFormats.First(format => format.Value == "mdl");
        _selectedAxisMode = AxisModes[0];
        _selectedPhysicsMode = PhysicsModes[0];
        _selectedExplorerProfile = ExplorerProfiles[0];
        TryLoadDefaultConfig();
        TryLoadSettings();
        _suppressSettingsSave = false;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null && _persistedPropertyNames.Contains(e.PropertyName))
            {
                QueueSettingsSave();
            }
        };
    }

    public ObservableCollection<DisplayOption> InputFormats { get; } =
    [
        new("opt", "OPT", new OPTImporter().InputName),
        new("mdl", "MDL", new MDLImporter().InputName),
        new("psk", "PSK", new PSKImporter().InputName),
        new("mow", "MOW", new MOWImporter().InputName)
    ];

    public ObservableCollection<DisplayOption> OutputFormats { get; } =
    [
        new("info", "Info", "Summary"),
        new(new OBJExporter().OutputFormat, "OBJ", new OBJExporter().OutputName),
        new("glb", "GLB", new GLTFExporter().OutputName),
        new("gltf", "glTF", new GLTFExporter().OutputName),
        new("source", "Source", new MDLExporter().OutputName),
        new(new MDLExporter().OutputFormat, "MDL", new MDLExporter().OutputName)
    ];

    public ObservableCollection<DisplayOption> AxisModes { get; } =
    [
        new("auto", "Auto", string.Empty),
        new("z-up", "Z Up", string.Empty),
        new("y-up", "Y Up", string.Empty)
    ];

    public ObservableCollection<DisplayOption> ExplorerProfiles { get; } =
    [
        new(ExplorerService.AutoProfileId, "Auto-detect", string.Empty)
    ];

    public ObservableCollection<DisplayOption> PhysicsModes { get; } =
    [
        new("bounds", "Bounds", string.Empty),
        new("coacd", "CoACD", string.Empty)
    ];

    public ObservableCollection<ExplorerNode> ExplorerNodes { get; } = [];

    public ObservableCollection<string> LogLines => _logSink.Lines;

    public bool HasSelectedExplorerEntry => SelectedExplorerNode?.Entry is not null;

    public bool IsSourceOutput => SelectedOutputFormat.Value is "source" or "mdl";

    public bool IsPskInput => SelectedInputFormat.Value is "psk";

    public bool IsPhysicsEnabled => IsSourceOutput && GeneratePhysics;

    public bool IsCoacdEnabled => IsSourceOutput && GeneratePhysics && SelectedPhysicsMode.Value is "coacd";

    public bool IsPreviewEmpty => string.IsNullOrWhiteSpace(PreviewModelPath);

    public bool HasPreviewPhysics => !string.IsNullOrWhiteSpace(PreviewPhysicsModelPath);

    public bool IsIdle => !IsBusy;

    public bool CanBrowseAnimation => IsIdle && IsPskInput;

    public bool HasExplorerFilter => !string.IsNullOrWhiteSpace(ExplorerFilter);

    partial void OnSelectedOutputFormatChanged(DisplayOption value)
    {
        OnPropertyChanged(nameof(IsSourceOutput));
        OnPropertyChanged(nameof(IsPhysicsEnabled));
        OnPropertyChanged(nameof(IsCoacdEnabled));
    }

    partial void OnSelectedInputFormatChanged(DisplayOption value)
    {
        OnPropertyChanged(nameof(IsPskInput));
        OnPropertyChanged(nameof(CanBrowseAnimation));
    }

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

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanBrowseAnimation));
        RunConversionCommand.NotifyCanExecuteChanged();
        LoadPreviewCommand.NotifyCanExecuteChanged();
        LoadConfigCommand.NotifyCanExecuteChanged();
        ScanExplorerCommand.NotifyCanExecuteChanged();
        RefreshExplorerCommand.NotifyCanExecuteChanged();
        PreviewExplorerSelectionCommand.NotifyCanExecuteChanged();
        ExportExplorerSelectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnGeneratePhysicsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPhysicsEnabled));
    }

    partial void OnSelectedPhysicsModeChanged(DisplayOption value)
    {
        OnPropertyChanged(nameof(IsPhysicsEnabled));
        OnPropertyChanged(nameof(IsCoacdEnabled));
    }

    partial void OnExplorerFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasExplorerFilter));
        ClearExplorerFilterCommand.NotifyCanExecuteChanged();
        QueueExplorerFilterRebuild();
    }

    partial void OnPreviewModelPathChanged(string value)
    {
        OnPropertyChanged(nameof(IsPreviewEmpty));
    }

    partial void OnPreviewPhysicsModelPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasPreviewPhysics));
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task RunConversionAsync()
    {
        await RunBusyAsync("Running conversion...", () =>
        {
            var result = _conversionService.RunConversion(CaptureSettings());
            _logSink.Append(result);
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private void LoadConfig()
    {
        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
            _logSink.Append("Config path is empty.");
            StatusMessage = "Config path is empty.";
            return;
        }

        try
        {
            ApplyConfig(UiConfig.Load(ConfigPath));
            ConfigPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ConfigPath));
            StatusMessage = "Config loaded.";
            _logSink.Append($"Loaded config: {ConfigPath}");
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "Config load failed.";
            _logSink.Append($"Config load failed. {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task LoadPreviewAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await LoadPreviewCoreAsync();
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "Preview failed.";
            _logSink.Append(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
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

    private async Task ScanExplorerAsync(bool clearCaches)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        var message = clearCaches ? "Refreshing explorer..." : "Scanning explorer root...";
        StatusMessage = message;
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
            StatusMessage = clearCaches ? "Explorer refresh complete." : "Explorer scan complete.";
            _logSink.Append($"Explorer found {result.Entries.Count} supported model file(s) using {result.Profile.DisplayName}.");
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ExplorerStatus = "Scan failed.";
            StatusMessage = "Explorer scan failed.";
            _logSink.Append(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseExplorerSelection))]
    private async Task PreviewExplorerSelectionAsync()
    {
        if (IsBusy ||
            SelectedExplorerNode?.Entry is not { } entry)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await ApplyExplorerSelectionAsync(entry);
            await LoadPreviewCoreAsync();
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "Explorer preview failed.";
            ExplorerStatus = "Preview failed.";
            _logSink.Append($"Explorer preview failed. {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseExplorerSelection))]
    private async Task ExportExplorerSelectionAsync()
    {
        if (IsBusy ||
            SelectedExplorerNode?.Entry is not { } entry)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await ApplyExplorerSelectionAsync(entry);
            StatusMessage = "Running conversion...";
            _logSink.Append("Running conversion...");
            var result = await Task.Run(() => _conversionService.RunConversion(CaptureSettings()));
            _logSink.Append(result);
            StatusMessage = "Done.";
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "Explorer export failed.";
            ExplorerStatus = "Export failed.";
            _logSink.Append($"Explorer export failed. {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasExplorerFilter))]
    private void ClearExplorerFilter()
    {
        ExplorerFilter = string.Empty;
        _explorerFilterCts?.Cancel();
        RebuildExplorerNodes();
    }

    private bool CanUseExplorerSelection()
    {
        return HasSelectedExplorerEntry && !IsBusy;
    }

    private bool CanRunCommand()
    {
        return !IsBusy;
    }

    private async Task ApplyExplorerSelectionAsync(ExplorerFileEntry entry)
    {
        var resolveMessage = entry.ArchivePath is null
            ? "Preparing explorer selection..."
            : "Resolving archive model and textures...";
        StatusMessage = resolveMessage;
        ExplorerStatus = resolveMessage;
        _logSink.Append(resolveMessage);

        var resolvedEntry = await Task.Run(() => _explorerService.ResolveEntry(entry));
        PopulateExplorerSelection(entry, resolvedEntry);
        ModelPath = $"gmconverter/{SanitizePathToken(BaseName)}.mdl";
        ExplorerStatus = $"Prepared {entry.DisplayPath}.";
    }

    private async Task LoadPreviewCoreAsync()
    {
        StatusMessage = "Loading preview...";
        _logSink.Append("Loading preview...");
        PreviewPhysicsModelPath = string.Empty;

        var result = await Task.Run(() => _conversionService.LoadPreview(CaptureSettings()));
        PreviewText = result.Summary.ToString();
        PreviewPhysicsModelPath = result.PhysicsModelPath ?? string.Empty;
        PreviewModelPath = result.ModelPath;
        StatusMessage = "Preview loaded.";
        _logSink.Append("Preview loaded.");
    }

    private void PopulateExplorerSelection(ExplorerFileEntry fileEntry, ExplorerResolvedEntry? resolvedEntry = null)
    {
        var inputPath = resolvedEntry?.InputPath ?? fileEntry.FilePath;
        var materialDirectory = resolvedEntry?.MaterialDirectory ?? fileEntry.MaterialDirectory;

        SelectedInputFormat = InputFormats.First(format => format.Value == fileEntry.InputFormat);
        InputPath = inputPath;
        MaterialDirectory = materialDirectory;
        BaseName = Path.GetFileNameWithoutExtension(inputPath);
        ModelPath = $"gmconverter/{SanitizePathToken(BaseName)}.mdl";
        SelectedExplorerDetails = FormatExplorerDetails(fileEntry);
    }

    private async Task RunBusyAsync(string message, Action action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = message;
        _logSink.Append(message);
        try
        {
            await Task.Run(action);
            StatusMessage = "Done.";
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "Operation failed.";
            _logSink.Append(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private ConversionSettings CaptureSettings()
    {
        return new ConversionSettings(
            SelectedInputFormat.Value,
            SelectedOutputFormat.Value,
            InputPath,
            string.IsNullOrWhiteSpace(OutputPath) ? null : OutputPath,
            string.IsNullOrWhiteSpace(BaseName) ? null : BaseName,
            IsSourceOutput && !string.IsNullOrWhiteSpace(ModelPath) ? ModelPath : null,
            IsSourceOutput && !string.IsNullOrWhiteSpace(GameDirectory) ? GameDirectory : null,
            IsSourceOutput && !string.IsNullOrWhiteSpace(EngineDirectory) ? EngineDirectory : null,
            string.IsNullOrWhiteSpace(MaterialDirectory) ? null : MaterialDirectory,
            IsPskInput && !string.IsNullOrWhiteSpace(AnimationPath) ? AnimationPath : null,
            (float)ScaleFactor,
            ConversionService.NormalizeAxisMode(SelectedAxisMode.Value),
            BuildMaterials,
            GeneratePhysics,
            GeneratePhysics ? SelectedPhysicsMode.Value : null,
            (float)PhysicsMass,
            (float)CoacdThreshold,
            MaxConvexPieces,
            MaxHullVertices);
    }

    private void TryLoadDefaultConfig()
    {
        var defaultPath = UiConfig.FindDefaultPath();
        if (defaultPath is null)
        {
            return;
        }

        ConfigPath = defaultPath;
        LoadConfig();
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
        SetSelected(InputFormats, settings.InputFormat, value => SelectedInputFormat = value);
        SetSelected(OutputFormats, settings.OutputFormat, value => SelectedOutputFormat = value);
        SetSelected(AxisModes, settings.AxisMode, value => SelectedAxisMode = value);
        SetSelected(ExplorerProfiles, settings.ExplorerProfile, value => SelectedExplorerProfile = value);
        SetSelected(PhysicsModes, settings.PhysicsMode, value => SelectedPhysicsMode = value);

        ConfigPath = settings.ConfigPath ?? ConfigPath;
        InputPath = settings.InputPath ?? InputPath;
        AnimationPath = settings.AnimationPath ?? AnimationPath;
        OutputPath = settings.OutputPath ?? OutputPath;
        BaseName = settings.BaseName ?? BaseName;
        ModelPath = settings.ModelPath ?? ModelPath;
        GameDirectory = settings.GameDirectory ?? GameDirectory;
        EngineDirectory = settings.EngineDirectory ?? EngineDirectory;
        MaterialDirectory = settings.MaterialDirectory ?? MaterialDirectory;
        ExplorerRootDirectory = settings.ExplorerRootDirectory ?? ExplorerRootDirectory;
        ExplorerFilter = settings.ExplorerFilter ?? ExplorerFilter;
        ScaleFactor = settings.ScaleFactor;
        BuildMaterials = settings.BuildMaterials;
        GeneratePhysics = settings.GeneratePhysics;
        PreviewOrthographic = settings.PreviewOrthographic;
        PreviewWireframe = settings.PreviewWireframe;
        PreviewPhysicsOverlay = settings.PreviewPhysicsOverlay;
        PhysicsMass = settings.PhysicsMass;
        if (HasLegacyCoacdDefaults(settings))
        {
            CoacdThreshold = _defaultCoacdThreshold;
            MaxConvexPieces = _defaultMaxConvexPieces;
            MaxHullVertices = _defaultMaxHullVertices;
        }
        else
        {
            CoacdThreshold = settings.CoacdThreshold;
            MaxConvexPieces = settings.MaxConvexPieces;
            MaxHullVertices = settings.MaxHullVertices;
        }
    }

    private UiSettings CaptureSettingsState()
    {
        return new UiSettings(
            SelectedInputFormat.Value,
            SelectedOutputFormat.Value,
            SelectedAxisMode.Value,
            SelectedExplorerProfile.Value,
            SelectedPhysicsMode.Value,
            EmptyToNull(ConfigPath),
            EmptyToNull(InputPath),
            EmptyToNull(AnimationPath),
            EmptyToNull(OutputPath),
            EmptyToNull(BaseName),
            EmptyToNull(ModelPath),
            EmptyToNull(GameDirectory),
            EmptyToNull(EngineDirectory),
            EmptyToNull(MaterialDirectory),
            EmptyToNull(ExplorerRootDirectory),
            EmptyToNull(ExplorerFilter),
            ScaleFactor,
            BuildMaterials,
            GeneratePhysics,
            PreviewOrthographic,
            PreviewWireframe,
            PreviewPhysicsOverlay,
            PhysicsMass,
            CoacdThreshold,
            MaxConvexPieces,
            MaxHullVertices);
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
        _explorerFilterCts?.Cancel();
        _explorerFilterCts?.Dispose();
        _explorerFilterCts = null;
        GC.SuppressFinalize(this);
    }

    private void ApplyConfig(UiConfig config)
    {
        SetSelected(InputFormats, config.InputFormat, value => SelectedInputFormat = value);
        SetSelected(OutputFormats, config.OutputFormat, value => SelectedOutputFormat = value);
        SetSelected(AxisModes, config.AxisMode, value => SelectedAxisMode = value);
        SetSelected(PhysicsModes, config.PhysicsMode, value => SelectedPhysicsMode = value);

        SetText(config.InputPath, value => InputPath = value);
        SetText(config.OutputPath, value => OutputPath = value);
        SetText(config.BaseName, value => BaseName = value);
        SetText(config.ModelPath, value => ModelPath = value);
        SetText(config.GameDirectory, value => GameDirectory = value);
        SetText(config.EngineDirectory, value => EngineDirectory = value);
        SetText(config.MaterialDirectory, value => MaterialDirectory = value);
        SetText(config.AnimationPath, value => AnimationPath = value);

        if (config.Scale.HasValue)
        {
            ScaleFactor = config.Scale.Value;
        }

        if (config.NoScale is true)
        {
            ScaleFactor = 1.0;
        }

        if (config.NoMaterials.HasValue)
        {
            BuildMaterials = !config.NoMaterials.Value;
        }

        if (config.Physics.HasValue)
        {
            GeneratePhysics = config.Physics.Value;
        }

        if (config.PhysicsMass.HasValue)
        {
            PhysicsMass = config.PhysicsMass.Value;
        }

        if (config.CoacdThreshold.HasValue)
        {
            CoacdThreshold = config.CoacdThreshold.Value;
        }

        if (config.MaxConvexPieces.HasValue)
        {
            MaxConvexPieces = config.MaxConvexPieces.Value;
        }

        if (config.MaxHullVertices.HasValue)
        {
            MaxHullVertices = config.MaxHullVertices.Value;
        }
    }

    private static void SetText(string? value, Action<string> set)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            set(value);
        }
    }

    private static void SetSelected(
        IEnumerable<DisplayOption> options,
        string? value,
        Action<DisplayOption> set)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var option = options.FirstOrDefault(item =>
            string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Label, value, StringComparison.OrdinalIgnoreCase));
        if (option is not null)
        {
            set(option);
        }
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

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool HasLegacyCoacdDefaults(UiSettings settings)
    {
        return Math.Abs(settings.CoacdThreshold - _legacyDefaultCoacdThreshold) < 0.000001 &&
            settings.MaxConvexPieces == _legacyDefaultMaxConvexPieces &&
            settings.MaxHullVertices == _legacyDefaultMaxHullVertices;
    }

    private static string SanitizePathToken(string value)
    {
        return string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? char.ToLowerInvariant(c) : '_')).Trim('_');
    }
}
