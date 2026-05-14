using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GMConverter.Common;
using GMConverter.Explorer;
using GMConverter.Exporters;
using GMConverter.Importers;
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
    private double _scaleFactor = 1.0;

    [ObservableProperty]
    private bool _buildMaterials = true;

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
        _selectedOutputFormat = OutputFormats.First(format => format.Value == "mdl");
        _selectedAxisMode = AxisModes[0];
        _selectedPhysicsMode = PhysicsModes[0];
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

    public ObservableCollection<DisplayOption> PhysicsModes { get; } =
    [
        new("bounds", "Bounds", string.Empty),
        new("coacd", "CoACD", string.Empty)
    ];

    public bool IsSourceOutput => SelectedOutputFormat.Value is "source" or "mdl";

    public bool IsPskInput => SelectedInputFormat.Value is "psk";

    public bool IsPhysicsEnabled => IsSourceOutput && GeneratePhysics;

    public bool IsCoacdEnabled => IsSourceOutput && GeneratePhysics && SelectedPhysicsMode.Value is "coacd";

    public bool IsIdle => !_getIsBusy();

    public bool CanBrowseAnimation => IsIdle && IsPskInput;

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

    partial void OnGeneratePhysicsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPhysicsEnabled));
        OnPropertyChanged(nameof(IsCoacdEnabled));
    }

    partial void OnSelectedPhysicsModeChanged(DisplayOption value)
    {
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

        var result = await Task.Run(() => _conversionService.LoadPreview(CaptureSettings()));
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
        GameDirectory = settings.GameDirectory ?? GameDirectory;
        EngineDirectory = settings.EngineDirectory ?? EngineDirectory;
        MaterialDirectory = settings.MaterialDirectory ?? MaterialDirectory;
        ScaleFactor = settings.ScaleFactor;
        BuildMaterials = settings.BuildMaterials;
        GeneratePhysics = settings.GeneratePhysics;
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
