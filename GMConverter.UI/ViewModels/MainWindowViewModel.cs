using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GMConverter.Common;
using GMConverter.Exporters;
using GMConverter.Explorer;
using GMConverter.Importers;
using GMConverter.UI.Models;
using GMConverter.UI.Services;

namespace GMConverter.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const double DefaultCoacdThreshold = 0.05;
    private const int DefaultMaxConvexPieces = 16;
    private const int DefaultMaxHullVertices = 16;
    private const double LegacyDefaultCoacdThreshold = 0.01;
    private const int LegacyDefaultMaxConvexPieces = 32;
    private const int LegacyDefaultMaxHullVertices = 32;

    private static readonly HashSet<string> PersistedPropertyNames = new(StringComparer.Ordinal)
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

    private readonly UiLogSink logSink = new();
    private readonly ConversionService conversionService;
    private readonly ExplorerService explorerService = new();
    private readonly List<ExplorerFileEntry> explorerEntries = [];
    private CancellationTokenSource? settingsSaveCts;
    private CancellationTokenSource? explorerFilterCts;
    private bool suppressSettingsSave;
    private string explorerProfileName = "Explorer";

    [ObservableProperty]
    private DisplayOption selectedInputFormat;

    [ObservableProperty]
    private DisplayOption selectedOutputFormat;

    [ObservableProperty]
    private DisplayOption selectedAxisMode;

    [ObservableProperty]
    private DisplayOption selectedExplorerProfile;

    [ObservableProperty]
    private ExplorerNode? selectedExplorerNode;

    [ObservableProperty]
    private DisplayOption selectedPhysicsMode;

    [ObservableProperty]
    private string configPath = string.Empty;

    [ObservableProperty]
    private string inputPath = string.Empty;

    [ObservableProperty]
    private string animationPath = string.Empty;

    [ObservableProperty]
    private string outputPath = string.Empty;

    [ObservableProperty]
    private string baseName = string.Empty;

    [ObservableProperty]
    private string modelPath = "gmconverter/model.mdl";

    [ObservableProperty]
    private string gameDirectory = string.Empty;

    [ObservableProperty]
    private string engineDirectory = string.Empty;

    [ObservableProperty]
    private string materialDirectory = string.Empty;

    [ObservableProperty]
    private string explorerRootDirectory = string.Empty;

    [ObservableProperty]
    private string explorerFilter = string.Empty;

    [ObservableProperty]
    private string selectedExplorerDetails = "Select a model in the tree.";

    [ObservableProperty]
    private double scaleFactor = 1.0;

    [ObservableProperty]
    private bool buildMaterials = true;

    [ObservableProperty]
    private bool generatePhysics;

    [ObservableProperty]
    private bool previewOrthographic;

    [ObservableProperty]
    private bool previewWireframe;

    [ObservableProperty]
    private bool previewPhysicsOverlay;

    [ObservableProperty]
    private double physicsMass = 100.0;

    [ObservableProperty]
    private double coacdThreshold = DefaultCoacdThreshold;

    [ObservableProperty]
    private int maxConvexPieces = DefaultMaxConvexPieces;

    [ObservableProperty]
    private int maxHullVertices = DefaultMaxHullVertices;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string previewText = "No preview loaded.";

    [ObservableProperty]
    private string previewModelPath = string.Empty;

    [ObservableProperty]
    private string previewPhysicsModelPath = string.Empty;

    [ObservableProperty]
    private string statusMessage = "Ready.";

    [ObservableProperty]
    private string explorerStatus = "Select a root directory and scan for supported models.";

    public MainWindowViewModel()
    {
        suppressSettingsSave = true;
        conversionService = new ConversionService(logSink);
        foreach (var profile in explorerService.Profiles)
        {
            ExplorerProfiles.Add(new DisplayOption(profile.Id, profile.DisplayName, string.Empty));
        }

        selectedInputFormat = InputFormats[0];
        selectedOutputFormat = OutputFormats.First(format => format.Value == "mdl");
        selectedAxisMode = AxisModes[0];
        selectedPhysicsMode = PhysicsModes[0];
        selectedExplorerProfile = ExplorerProfiles[0];
        TryLoadDefaultConfig();
        TryLoadSettings();
        suppressSettingsSave = false;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null && PersistedPropertyNames.Contains(e.PropertyName))
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

    public ObservableCollection<string> LogLines => logSink.Lines;

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
            var result = conversionService.RunConversion(CaptureSettings());
            logSink.Append(result);
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private void LoadConfig()
    {
        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
            logSink.Append("Config path is empty.");
            StatusMessage = "Config path is empty.";
            return;
        }

        try
        {
            ApplyConfig(UiConfig.Load(ConfigPath));
            ConfigPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ConfigPath));
            StatusMessage = "Config loaded.";
            logSink.Append($"Loaded config: {ConfigPath}");
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "Config load failed.";
            logSink.Append($"Config load failed. {ex.Message}");
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
            logSink.Append(ex.Message);
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
        logSink.Append(message);
        try
        {
            var result = await Task.Run(() =>
            {
                if (clearCaches)
                {
                    explorerService.ClearCaches();
                }

                return explorerService.Scan(ExplorerRootDirectory, SelectedExplorerProfile.Value);
            });
            explorerEntries.Clear();
            explorerEntries.AddRange(result.Entries);
            explorerProfileName = result.Profile.DisplayName;
            RebuildExplorerNodes();
            StatusMessage = clearCaches ? "Explorer refresh complete." : "Explorer scan complete.";
            logSink.Append($"Explorer found {result.Entries.Count} supported model file(s) using {result.Profile.DisplayName}.");
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ExplorerStatus = "Scan failed.";
            StatusMessage = "Explorer scan failed.";
            logSink.Append(ex.Message);
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
            logSink.Append($"Explorer preview failed. {ex.Message}");
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
            logSink.Append("Running conversion...");
            var result = await Task.Run(() => conversionService.RunConversion(CaptureSettings()));
            logSink.Append(result);
            StatusMessage = "Done.";
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "Explorer export failed.";
            ExplorerStatus = "Export failed.";
            logSink.Append($"Explorer export failed. {ex.Message}");
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
        explorerFilterCts?.Cancel();
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
        logSink.Append(resolveMessage);

        var resolvedEntry = await Task.Run(() => explorerService.ResolveEntry(entry));
        PopulateExplorerSelection(entry, resolvedEntry);
        ModelPath = $"gmconverter/{SanitizePathToken(BaseName)}.mdl";
        ExplorerStatus = $"Prepared {entry.DisplayPath}.";
    }

    private async Task LoadPreviewCoreAsync()
    {
        StatusMessage = "Loading preview...";
        logSink.Append("Loading preview...");
        PreviewPhysicsModelPath = string.Empty;

        var result = await Task.Run(() => conversionService.LoadPreview(CaptureSettings()));
        PreviewText = result.Summary.ToString();
        PreviewPhysicsModelPath = result.PhysicsModelPath ?? string.Empty;
        PreviewModelPath = result.ModelPath;
        StatusMessage = "Preview loaded.";
        logSink.Append("Preview loaded.");
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
        logSink.Append(message);
        try
        {
            await Task.Run(action);
            StatusMessage = "Done.";
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "Operation failed.";
            logSink.Append(ex.Message);
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
                logSink.Append($"Loaded UI settings: {UiSettings.SettingsPath}");
            }
        }
        catch (GMConverterException ex)
        {
            logSink.Append(ex.Message);
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
            CoacdThreshold = DefaultCoacdThreshold;
            MaxConvexPieces = DefaultMaxConvexPieces;
            MaxHullVertices = DefaultMaxHullVertices;
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
        if (suppressSettingsSave)
        {
            return;
        }

        settingsSaveCts?.Cancel();
        settingsSaveCts = new CancellationTokenSource();
        var token = settingsSaveCts.Token;
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
            logSink.Append($"Failed to save UI settings. {ex.Message}");
        }
    }

    public void SaveSettingsNow()
    {
        if (suppressSettingsSave)
        {
            return;
        }

        try
        {
            settingsSaveCts?.Cancel();
            CaptureSettingsState().Save();
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException)
        {
            logSink.Append($"Failed to save UI settings. {ex.Message}");
        }
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

        if (current is not null)
        {
            current.MarkAsModel(fileEntry);
        }
    }

    private void RebuildExplorerNodes()
    {
        explorerFilterCts?.Cancel();
        ExplorerNodes.Clear();
        SelectedExplorerNode = null;

        var filteredEntries = FilterExplorerEntries().ToArray();
        foreach (var entry in filteredEntries)
        {
            AddExplorerEntry(entry);
        }

        ExplorerStatus = string.IsNullOrWhiteSpace(ExplorerFilter)
            ? $"{explorerProfileName}: {explorerEntries.Count} supported model file(s)."
            : $"{explorerProfileName}: {filteredEntries.Length} of {explorerEntries.Count} supported model file(s).";
    }

    private IEnumerable<ExplorerFileEntry> FilterExplorerEntries()
    {
        var terms = ExplorerFilter
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return explorerEntries;
        }

        return explorerEntries.Where(entry => terms.All(term =>
            entry.DisplayPath.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private void QueueExplorerFilterRebuild()
    {
        explorerFilterCts?.Cancel();

        if (explorerEntries.Count == 0)
        {
            return;
        }

        explorerFilterCts = new CancellationTokenSource();
        var token = explorerFilterCts.Token;
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
        return Math.Abs(settings.CoacdThreshold - LegacyDefaultCoacdThreshold) < 0.000001 &&
            settings.MaxConvexPieces == LegacyDefaultMaxConvexPieces &&
            settings.MaxHullVertices == LegacyDefaultMaxHullVertices;
    }

    private static string SanitizePathToken(string value)
    {
        return string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? char.ToLowerInvariant(c) : '_')).Trim('_');
    }
}
