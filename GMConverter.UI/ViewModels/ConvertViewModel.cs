using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GMConverter.Exporters;
using GMConverter.Importers;
using GMConverter.Plugins;
using GMConverter.SDK.Common;
using GMConverter.SDK.Explorer;
using GMConverter.SDK.Importers;
using GMConverter.UI.Models;
using GMConverter.UI.Services;

namespace GMConverter.UI.ViewModels;

public sealed partial class ConvertViewModel : ViewModelBase
{
    private const double _defaultCoacdThreshold = 0.05;
    private const int _defaultMaxConvexPieces = 16;
    private const int _defaultMaxHullVertices = 16;
    private const double _legacyDefaultCoacdThreshold = 0.01;
    private const int _legacyDefaultMaxConvexPieces = 32;
    private const int _legacyDefaultMaxHullVertices = 32;

    private readonly UiLogSink _logSink;
    private readonly ConversionService _conversionService;
    private readonly Func<bool> _getIsBusy;
    private readonly Action<bool> _setIsBusy;
    private readonly Action<string> _setStatusMessage;
    private readonly Action<PreviewLoadResult, string> _onPreviewLoaded;

    [ObservableProperty]
    private DisplayOption _selectedInputFormat;

    [ObservableProperty]
    private DisplayOption _selectedOutputFormat;

    [ObservableProperty]
    private DisplayOption _selectedAxisMode;

    [ObservableProperty]
    private DisplayOption _selectedPhysicsMode;

    [ObservableProperty]
    private DisplayOption _selectedMaxTextureSize;

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
    private string _studioMdlPath = string.Empty;

    [ObservableProperty]
    private string _vtfCmdPath = string.Empty;

    [ObservableProperty]
    private string _materialDirectory = string.Empty;

    [ObservableProperty]
    private double _scaleFactor = 1.0;

    [ObservableProperty]
    private bool _buildMaterials = true;

    [ObservableProperty]
    private bool _deduplicateTextures = true;

    [ObservableProperty]
    private bool _generatePhysics;

    [ObservableProperty]
    private double _physicsMass = 100.0;

    [ObservableProperty]
    private double _coacdThreshold = _defaultCoacdThreshold;

    [ObservableProperty]
    private int _maxConvexPieces = _defaultMaxConvexPieces;

    [ObservableProperty]
    private int _maxHullVertices = _defaultMaxHullVertices;

    internal ConvertViewModel(
        UiLogSink logSink,
        ConversionService conversionService,
        Func<bool> getIsBusy,
        Action<bool> setIsBusy,
        Action<string> setStatusMessage,
        Action<PreviewLoadResult, string> onPreviewLoaded)
    {
        _logSink = logSink;
        _conversionService = conversionService;
        _getIsBusy = getIsBusy;
        _setIsBusy = setIsBusy;
        _setStatusMessage = setStatusMessage;
        _onPreviewLoaded = onPreviewLoaded;

        _selectedInputFormat = InputFormats[0];
        _selectedOutputFormat = OutputFormats.FirstOrDefault(format => format.Value == "mdl")
            ?? OutputFormats.First(format => format.Value == "glb");
        _selectedAxisMode = AxisModes[0];
        _selectedPhysicsMode = PhysicsModes[0];
        _selectedMaxTextureSize = MaxTextureSizes.First(option => option.Value == "1024");
        RefreshCurrentExporterOptions();
    }

    public ObservableCollection<DisplayOption> InputFormats { get; } =
        [.. new IImporter[] { new OPTImporter(), new MOWImporter() }
            .Concat(PluginHost.Registry.Importers)
            .DistinctBy(importer => importer.InputFormat, StringComparer.OrdinalIgnoreCase)
            .Select(importer => new DisplayOption(importer.InputFormat, importer.InputFormat.ToUpperInvariant(), importer.InputName))];

    public ObservableCollection<DisplayOption> OutputFormats { get; } =
    [
        new("info", "Info", "Summary"),
        new("obj", "OBJ", new OBJExporter().OutputName),
        new("glb", "GLB", new GLTFExporter().OutputName),
        new("gltf", "glTF", new GLTFExporter().OutputName),
        .. PluginHost.Registry.Exporters
            .Where(exporter => exporter.OutputFormat is not ("info" or "obj" or "glb" or "gltf"))
            .Select(exporter => new DisplayOption(exporter.OutputFormat, exporter.OutputFormat.ToUpperInvariant(), exporter.OutputName)),
        .. PluginHost.Registry.GetExporter("mdl") is { } source
            ? new[] { new DisplayOption("source", "Source", source.OutputName) }
            : []
    ];

    public ObservableCollection<DisplayOption> AxisModes { get; } =
    [
        new("auto", "Auto", string.Empty),
        new("z-up", "Z Up", string.Empty),
        new("y-up", "Y Up", string.Empty)
    ];

    public ObservableCollection<DisplayOption> PhysicsModes { get; } =
    [
        new("bounds", "Bounds", string.Empty),
        new("coacd", "CoACD", string.Empty)
    ];

    public ObservableCollection<DisplayOption> MaxTextureSizes { get; } =
    [
        new("0", "Original", "no resize"),
        new("512", "512", string.Empty),
        new("1024", "1024", string.Empty),
        new("2048", "2048", string.Empty),
        new("4096", "4096", string.Empty)
    ];

    public bool IsSourceOutput => SelectedOutputFormat.Value is "source" or "mdl";

    public bool IsPskInput => SelectedInputFormat.Value is "psk";

    public bool IsPhysicsEnabled => IsSourceOutput && GeneratePhysics;

    public bool IsCoacdEnabled => IsSourceOutput && GeneratePhysics && SelectedPhysicsMode.Value is "coacd";

    public bool IsIdle => !_getIsBusy();

    public bool CanBrowseAnimation => IsIdle && IsPskInput;

    partial void OnSelectedOutputFormatChanged(DisplayOption value)
    {
        RefreshCurrentExporterOptions();
        OnPropertyChanged(nameof(IsSourceOutput));
        OnPropertyChanged(nameof(IsPhysicsEnabled));
        OnPropertyChanged(nameof(IsCoacdEnabled));
    }

    partial void OnSelectedInputFormatChanged(DisplayOption value)
    {
        OnPropertyChanged(nameof(IsPskInput));
        OnPropertyChanged(nameof(CanBrowseAnimation));
    }

    partial void OnGeneratePhysicsChanged(bool value)
    {
        PushTypedPropertyToBag("physics:enabled", value);
        OnPropertyChanged(nameof(IsPhysicsEnabled));
        OnPropertyChanged(nameof(IsCoacdEnabled));
    }

    partial void OnSelectedPhysicsModeChanged(DisplayOption value)
    {
        PushTypedPropertyToBag("physics:mode", value?.Value);
        OnPropertyChanged(nameof(IsPhysicsEnabled));
        OnPropertyChanged(nameof(IsCoacdEnabled));
    }

    internal void NotifyBusyChanged()
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanBrowseAnimation));
        RunConversionCommand.NotifyCanExecuteChanged();
        LoadPreviewCommand.NotifyCanExecuteChanged();
        LoadConfigCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task RunConversionAsync()
    {
        var settings = CaptureSettings();
        await RunBusyAsync("Running conversion...", () =>
        {
            var result = _conversionService.RunConversion(settings);
            _logSink.Append(result);
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private void LoadConfig()
    {
        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
            _logSink.Append("Config path is empty.");
            _setStatusMessage("Config path is empty.");
            return;
        }

        try
        {
            ApplyConfig(UiConfig.Load(ConfigPath));
            ConfigPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ConfigPath));
            _setStatusMessage("Config loaded.");
            _logSink.Append($"Loaded config: {ConfigPath}");
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _setStatusMessage("Config load failed.");
            _logSink.Append($"Config load failed. {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task LoadPreviewAsync()
    {
        if (_getIsBusy())
        {
            return;
        }

        _setIsBusy(true);
        try
        {
            await LoadPreviewCoreAsync();
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _setStatusMessage("Preview failed.");
            _logSink.Append(ex.Message);
        }
        finally
        {
            _setIsBusy(false);
        }
    }

    internal async Task LoadPreviewCoreAsync()
    {
        _setStatusMessage("Loading preview...");
        _logSink.Append("Loading preview...");

        var settings = CaptureSettings();
        var result = await Task.Run(() => _conversionService.LoadPreview(settings));
        _onPreviewLoaded(result, InputPath);
        _setStatusMessage("Preview loaded.");
        _logSink.Append("Preview loaded.");
    }

    internal ConversionSettings CaptureSettings()
    {
        return new ConversionSettings(
            SelectedInputFormat.Value,
            SelectedOutputFormat.Value,
            InputPath,
            string.IsNullOrWhiteSpace(OutputPath) ? null : OutputPath,
            string.IsNullOrWhiteSpace(BaseName) ? null : BaseName,
            IsSourceOutput && !string.IsNullOrWhiteSpace(ModelPath) ? ModelPath : null,
            IsSourceOutput && !string.IsNullOrWhiteSpace(StudioMdlPath) ? StudioMdlPath : null,
            IsSourceOutput && !string.IsNullOrWhiteSpace(VtfCmdPath) ? VtfCmdPath : null,
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
            MaxHullVertices,
            ParseMaxTextureSize(SelectedMaxTextureSize.Value),
            DeduplicateTextures,
            CurrentExporterOptions.BuildExportOptions());
    }

    private static int ParseMaxTextureSize(string value)
    {
        return int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? Math.Max(0, result)
            : 0;
    }

    // Fill empty StudioMDL / VTFCmd path fields from whatever's already extracted under tools/.
    // Called at startup while settings-save suppression is on, so these auto-discovered paths
    // never get persisted — if the user moves the app, the next launch re-resolves against the
    // new tools/ location instead of carrying a stale absolute path forward.
    internal void ApplyLocalToolDefaults()
    {
        var (studioMdl, vtfCmd) = TryFindLocalToolDefaults();
        if (string.IsNullOrWhiteSpace(StudioMdlPath) && studioMdl is not null)
        {
            StudioMdlPath = studioMdl;
        }
        if (string.IsNullOrWhiteSpace(VtfCmdPath) && vtfCmd is not null)
        {
            VtfCmdPath = vtfCmd;
        }
    }

    // Local copy of the path-discovery logic that used to live in GMConverter.Source.SourceToolPaths.
    // After Source extraction to a plugin the UI can't reference that type directly, and the UI's
    // auto-fill behavior shouldn't depend on the plugin being loaded — these directories follow a
    // convention (./tools/<tool>/...) that's stable across plugin presence. The plugin keeps its
    // own copy for the resolve path used at export time.
    private static (string? StudioMdl, string? VtfCmd) TryFindLocalToolDefaults()
    {
        return (
            FindExecutable(GetToolDirectory("studiomdl-ce"), "studiomdl.exe"),
            FindExecutable(GetToolDirectory("vtfedit-reloaded"), "VTFCmd.exe"));
    }

    private static string GetToolDirectory(string toolName)
    {
        return Path.Combine(AppContext.BaseDirectory, "tools", toolName);
    }

    private static string? FindExecutable(string root, string executableName)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }
        try
        {
            return Directory
                .EnumerateFiles(root, executableName, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
            return null;
        }
    }

    internal void TryLoadDefaultConfig()
    {
        var defaultPath = UiConfig.FindDefaultPath();
        if (defaultPath is null)
        {
            return;
        }

        ConfigPath = defaultPath;
        LoadConfig();
    }

    internal void ApplySettings(UiSettings settings)
    {
        SetSelected(InputFormats, settings.InputFormat, value => SelectedInputFormat = value);
        SetSelected(OutputFormats, settings.OutputFormat, value => SelectedOutputFormat = value);
        SetSelected(AxisModes, settings.AxisMode, value => SelectedAxisMode = value);
        SetSelected(PhysicsModes, settings.PhysicsMode, value => SelectedPhysicsMode = value);

        ConfigPath = settings.ConfigPath ?? ConfigPath;
        InputPath = settings.InputPath ?? InputPath;
        AnimationPath = settings.AnimationPath ?? AnimationPath;
        OutputPath = settings.OutputPath ?? OutputPath;
        BaseName = settings.BaseName ?? BaseName;
        ModelPath = settings.ModelPath ?? ModelPath;
        StudioMdlPath = settings.StudioMdlPath ?? StudioMdlPath;
        VtfCmdPath = settings.VtfCmdPath ?? VtfCmdPath;
        MaterialDirectory = settings.MaterialDirectory ?? MaterialDirectory;
        ScaleFactor = settings.ScaleFactor;
        BuildMaterials = settings.BuildMaterials;
        DeduplicateTextures = settings.DeduplicateTextures;
        GeneratePhysics = settings.GeneratePhysics;
        PhysicsMass = settings.PhysicsMass;

        var sizeValue = settings.MaxTextureSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sizeOption = MaxTextureSizes.FirstOrDefault(option => option.Value == sizeValue);
        if (sizeOption is not null)
        {
            SelectedMaxTextureSize = sizeOption;
        }

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
        LoadExporterOptions(settings.ExporterOptions);
    }

    internal void ApplyExplorerSelection(ExplorerFileEntry fileEntry, ExplorerResolvedEntry? resolvedEntry = null)
    {
        var inputPath = resolvedEntry?.InputPath ?? fileEntry.FilePath;
        var materialDirectory = resolvedEntry?.MaterialDirectory ?? fileEntry.MaterialDirectory;

        SelectedInputFormat = InputFormats.First(format => format.Value == fileEntry.InputFormat);
        InputPath = inputPath;
        MaterialDirectory = materialDirectory;
        AnimationPath = resolvedEntry?.AnimationPath ?? string.Empty;
        BaseName = Path.GetFileNameWithoutExtension(inputPath);
        ModelPath = $"gmconverter/{SanitizePathToken(BaseName)}.mdl";
    }

    private bool CanRunCommand()
    {
        return IsIdle;
    }

    private async Task RunBusyAsync(string message, Action action)
    {
        if (_getIsBusy())
        {
            return;
        }

        _setIsBusy(true);
        _setStatusMessage(message);
        _logSink.Append(message);
        try
        {
            await Task.Run(action);
            _setStatusMessage("Done.");
        }
        catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _setStatusMessage("Operation failed.");
            _logSink.Append(ex.Message);
        }
        finally
        {
            _setIsBusy(false);
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
        SetText(config.StudioMdlPath, value => StudioMdlPath = value);
        SetText(config.VtfCmdPath, value => VtfCmdPath = value);
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

        if (config.MaxTextureSize.HasValue)
        {
            var sizeValue = Math.Max(0, config.MaxTextureSize.Value)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            var sizeOption = MaxTextureSizes.FirstOrDefault(option => option.Value == sizeValue);
            if (sizeOption is not null)
            {
                SelectedMaxTextureSize = sizeOption;
            }
        }

        if (config.DeduplicateTextures.HasValue)
        {
            DeduplicateTextures = config.DeduplicateTextures.Value;
        }
    }

    private static void SetText(string? value, Action<string> set)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            set(value);
        }
    }

    internal static void SetSelected(
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

    private static bool HasLegacyCoacdDefaults(UiSettings settings)
    {
        return Math.Abs(settings.CoacdThreshold - _legacyDefaultCoacdThreshold) < 0.000001 &&
            settings.MaxConvexPieces == _legacyDefaultMaxConvexPieces &&
            settings.MaxHullVertices == _legacyDefaultMaxHullVertices;
    }

    internal static string SanitizePathToken(string value)
    {
        return string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? char.ToLowerInvariant(c) : '_')).Trim('_');
    }
}
