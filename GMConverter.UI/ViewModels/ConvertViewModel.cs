using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GMConverter.Exporters;
using GMConverter.Plugins;
using GMConverter.SDK.Common;
using GMConverter.SDK.Explorer;
using GMConverter.UI.Models;
using GMConverter.UI.Services;

namespace GMConverter.UI.ViewModels;

public sealed partial class ConvertViewModel : ViewModelBase
{
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
    private string _configPath = string.Empty;

    [ObservableProperty]
    private string _inputPath = string.Empty;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private string _baseName = string.Empty;

    [ObservableProperty]
    private string _materialDirectory = string.Empty;

    [ObservableProperty]
    private double _scaleFactor = 1.0;

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

        // Every importer comes from a plugin, so the list is empty when no plugins loaded.
        _selectedInputFormat = InputFormats.FirstOrDefault() ?? new DisplayOption("", "None", "No importer plugins loaded");
        _selectedOutputFormat = OutputFormats.FirstOrDefault(format => format.Value == "mdl")
            ?? OutputFormats.First(format => format.Value == "glb");
        _selectedAxisMode = AxisModes[0];
        RefreshCurrentExporterOptions();
        RefreshCurrentImporterOptions();
    }

    public ObservableCollection<DisplayOption> InputFormats { get; } =
        [.. PluginHost.Registry.Importers
            .DistinctBy(importer => importer.InputFormat, StringComparer.OrdinalIgnoreCase)
            .OrderBy(importer => importer.InputFormat, StringComparer.OrdinalIgnoreCase)
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

    public bool IsIdle => !_getIsBusy();

    partial void OnSelectedOutputFormatChanged(DisplayOption value)
    {
        RefreshCurrentExporterOptions();
    }

    partial void OnSelectedInputFormatChanged(DisplayOption value)
    {
        RefreshCurrentImporterOptions();
    }

    internal void NotifyBusyChanged()
    {
        OnPropertyChanged(nameof(IsIdle));
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
            string.IsNullOrWhiteSpace(MaterialDirectory) ? null : MaterialDirectory,
            (float)ScaleFactor,
            ConversionService.NormalizeAxisMode(SelectedAxisMode.Value),
            CurrentImporterOptions.BuildOptionValues(),
            CurrentExporterOptions.BuildOptionValues());
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

        ConfigPath = settings.ConfigPath ?? ConfigPath;
        InputPath = settings.InputPath ?? InputPath;
        OutputPath = settings.OutputPath ?? OutputPath;
        BaseName = settings.BaseName ?? BaseName;
        MaterialDirectory = settings.MaterialDirectory ?? MaterialDirectory;
        ScaleFactor = settings.ScaleFactor;

        LoadExporterOptions(settings.ExporterOptions);
        LoadImporterOptions(settings.ImporterOptions);
    }

    internal void ApplyExplorerSelection(ExplorerFileEntry fileEntry, ExplorerResolvedEntry? resolvedEntry = null)
    {
        var inputPath = resolvedEntry?.InputPath ?? fileEntry.FilePath;
        var materialDirectory = resolvedEntry?.MaterialDirectory ?? fileEntry.MaterialDirectory;

        SelectedInputFormat = InputFormats.First(format => format.Value == fileEntry.InputFormat);
        InputPath = inputPath;
        MaterialDirectory = materialDirectory;
        // Importer options describe the previous selection (e.g. its animation); start this one
        // from defaults plus whatever the explorer resolved alongside it.
        ResetCurrentImporterOptions();
        ApplyImporterOptions(resolvedEntry?.ImporterOptions);
        BaseName = Path.GetFileNameWithoutExtension(inputPath);
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

        SetText(config.InputPath, value => InputPath = value);
        SetText(config.OutputPath, value => OutputPath = value);
        SetText(config.BaseName, value => BaseName = value);
        SetText(config.MaterialDirectory, value => MaterialDirectory = value);

        if (config.Scale.HasValue)
        {
            ScaleFactor = config.Scale.Value;
        }

        if (config.NoScale is true)
        {
            ScaleFactor = 1.0;
        }

        foreach (var key in ApplyConfigOptionValues(config.OptionValues))
        {
            _logSink.Append($"Ignored config key '{key}': no setting or option for the selected formats uses it.");
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

}
