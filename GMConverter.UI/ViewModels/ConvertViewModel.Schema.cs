using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.SDK.Exporters;
using GMConverter.UI.Models;
using GMConverter.UI.Services;
using GMConverter.UI.ViewModels.Options;

namespace GMConverter.UI.ViewModels;

/// <summary>
/// Per-exporter option state, with a compatibility bridge for existing Source settings.
/// Plugin options are persisted by format without requiring host-specific properties.
/// </summary>
public sealed partial class ConvertViewModel
{
    private readonly Dictionary<string, ExporterOptionsViewModel> _exporterOptionsByFormat =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Dictionary<string, object?>> _persistedExporterOptions =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _syncingFromBag;
    private bool _syncingFromTyped;

    [ObservableProperty]
    private ExporterOptionsViewModel _currentExporterOptions =
        new("", ExporterOptionSchema.Empty);

    /// <summary>Lookup or create the option VM for the active output format and swap it in.</summary>
    private void RefreshCurrentExporterOptions()
    {
        var format = SelectedOutputFormat?.Value;
        if (format == "source")
        {
            format = "mdl";
        }
        if (string.IsNullOrEmpty(format))
        {
            CurrentExporterOptions = new ExporterOptionsViewModel("", ExporterOptionSchema.Empty);
            return;
        }

        if (!_exporterOptionsByFormat.TryGetValue(format, out var vm))
        {
            vm = new ExporterOptionsViewModel(format, GetSchemaFor(format));
            // Seed the bag from currently-set typed properties so the schema-driven panel
            // doesn't show empty defaults the first time the user opens it for this format.
            if (_persistedExporterOptions.TryGetValue(format, out var persisted))
            {
                vm.LoadFrom(persisted);
            }
            SeedBagFromTypedProperties(vm);
            SubscribeToBagChanges(vm);
            _exporterOptionsByFormat[format] = vm;
        }
        CurrentExporterOptions = vm;
    }

    private static ExporterOptionSchema GetSchemaFor(string format)
    {
        if (format == "info")
        {
            return ExporterOptionSchema.Empty;
        }
        var schema = ConversionService.GetExporter(format).OptionSchema;
        // The selected glTF format owns binary/text output; avoid a conflicting checkbox.
        return format is "glb" or "gltf"
            ? new ExporterOptionSchema([.. schema.Groups.Select(group => group with
            {
                Options = [.. group.Options.Where(option => option.Key != "binary")]
            })])
            : schema;
    }

    internal Dictionary<string, Dictionary<string, object?>> SnapshotExporterOptions()
    {
        var snapshot = _persistedExporterOptions.ToDictionary(pair => pair.Key,
            pair => new Dictionary<string, object?>(pair.Value), StringComparer.OrdinalIgnoreCase);
        foreach (var (format, vm) in _exporterOptionsByFormat)
        {
            if (!snapshot.TryGetValue(format, out var values))
            {
                values = [];
                snapshot[format] = values;
            }
            foreach (var (key, value) in vm.Snapshot())
            {
                // Source's existing JSON fields remain authoritative for legacy settings.
                if (format == "mdl" && TryReadTypedProperty(key, out _))
                {
                    values.Remove(key);
                }
                else
                {
                    values[key] = value;
                }
            }
        }
        return snapshot;
    }

    private void LoadExporterOptions(Dictionary<string, Dictionary<string, object?>>? persisted)
    {
        _persistedExporterOptions.Clear();
        if (persisted is not null)
        {
            foreach (var (format, values) in persisted)
            {
                _persistedExporterOptions[format] = new Dictionary<string, object?>(values);
            }
        }
        foreach (var (format, vm) in _exporterOptionsByFormat)
        {
            if (_persistedExporterOptions.TryGetValue(format, out var values))
            {
                _syncingFromTyped = true;
                try
                {
                    vm.LoadFrom(values);
                }
                finally
                {
                    _syncingFromTyped = false;
                }
            }
            SeedBagFromTypedProperties(vm);
        }
    }

    private void SeedBagFromTypedProperties(ExporterOptionsViewModel vm)
    {
        if (vm.ExporterFormat != "mdl")
        {
            return;
        }
        _syncingFromTyped = true;
        try
        {
            foreach (var group in vm.Groups)
            {
                foreach (var option in group.Options)
                {
                    if (TryReadTypedProperty(option.Key, out var value))
                    {
                        option.TryLoad(value);
                    }
                }
            }
        }
        finally
        {
            _syncingFromTyped = false;
        }
    }

    private void SubscribeToBagChanges(ExporterOptionsViewModel vm)
    {
        foreach (var group in vm.Groups)
        {
            foreach (var option in group.Options)
            {
                option.PropertyChanged += (sender, e) => OnBagOptionChanged(vm.ExporterFormat, sender, e);
            }
        }
    }

    private void OnBagOptionChanged(string format, object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingFromTyped || sender is not OptionViewModel option || e.PropertyName != "Value")
        {
            return;
        }
        _syncingFromBag = true;
        try
        {
            if (format == "mdl")
            {
                WriteTypedProperty(option.Key, option.GetCurrentValue());
            }
        }
        finally
        {
            _syncingFromBag = false;
        }
        OnPropertyChanged(nameof(CurrentExporterOptions));
    }

    private bool TryReadTypedProperty(string key, out object? value)
    {
        value = key switch
        {
            "modelPath" => ModelPath,
            "studioMdlPath" => StudioMdlPath,
            "vtfCmdPath" => VtfCmdPath,
            "buildMaterials" => BuildMaterials,
            "material:maxTextureSize" => SelectedMaxTextureSize?.Value,
            "material:deduplicateTextures" => DeduplicateTextures,
            "physics:enabled" => GeneratePhysics,
            "physics:mode" => SelectedPhysicsMode?.Value,
            "physics:mass" => (float)PhysicsMass,
            "physics:coacdThreshold" => (float)CoacdThreshold,
            "physics:maxConvexPieces" => MaxConvexPieces,
            "physics:maxHullVertices" => MaxHullVertices,
            _ => null,
        };
        return value is not null;
    }

    private void WriteTypedProperty(string key, object? value)
    {
        switch (key)
        {
            case "modelPath" when value is string s: ModelPath = s; break;
            case "studioMdlPath" when value is string s: StudioMdlPath = s; break;
            case "vtfCmdPath" when value is string s: VtfCmdPath = s; break;
            case "buildMaterials" when value is bool b: BuildMaterials = b; break;
            case "material:maxTextureSize" when value is string s:
                SetSelected(MaxTextureSizes, s, opt => SelectedMaxTextureSize = opt);
                break;
            case "material:deduplicateTextures" when value is bool b: DeduplicateTextures = b; break;
            case "physics:enabled" when value is bool b: GeneratePhysics = b; break;
            case "physics:mode" when value is string s:
                SetSelected(PhysicsModes, s, opt => SelectedPhysicsMode = opt);
                break;
            case "physics:mass" when value is float f: PhysicsMass = f; break;
            case "physics:mass" when value is double d: PhysicsMass = d; break;
            case "physics:coacdThreshold" when value is float f: CoacdThreshold = f; break;
            case "physics:coacdThreshold" when value is double d: CoacdThreshold = d; break;
            case "physics:maxConvexPieces" when value is int i: MaxConvexPieces = i; break;
            case "physics:maxHullVertices" when value is int i: MaxHullVertices = i; break;
        }
    }

    /// <summary>
    /// Push a legacy property change into Source's bag, including while another format is active.
    /// Called from <c>partial void OnXChanged</c> hooks on the typed properties.
    /// </summary>
    private void PushTypedPropertyToBag(string key, object? value)
    {
        if (_syncingFromBag || !_exporterOptionsByFormat.TryGetValue("mdl", out var vm))
        {
            return;
        }
        foreach (var group in vm.Groups)
        {
            var option = group.Options.FirstOrDefault(o => o.Key == key);
            if (option is not null)
            {
                _syncingFromTyped = true;
                try
                {
                    option.TryLoad(value);
                }
                finally
                {
                    _syncingFromTyped = false;
                }
                return;
            }
        }
    }

    // Hooks fired by the source-generated [ObservableProperty] setters on the main partial.
    // Each typed-property change is mirrored into Source's bag, even while another exporter
    // is selected, so config loads and Explorer selections keep the panel current. The reverse direction
    // (panel edit → typed property) is routed via OnBagOptionChanged above. The existing
    // OnSelectedPhysicsModeChanged + OnGeneratePhysicsChanged partials live on the main partial
    // and were extended there to call PushTypedPropertyToBag in addition to their original
    // computed-property refresh logic — a partial method can only have one implementation.
    partial void OnModelPathChanged(string value) => PushTypedPropertyToBag("modelPath", value);
    partial void OnStudioMdlPathChanged(string value) => PushTypedPropertyToBag("studioMdlPath", value);
    partial void OnVtfCmdPathChanged(string value) => PushTypedPropertyToBag("vtfCmdPath", value);
    partial void OnBuildMaterialsChanged(bool value) => PushTypedPropertyToBag("buildMaterials", value);
    partial void OnDeduplicateTexturesChanged(bool value) => PushTypedPropertyToBag("material:deduplicateTextures", value);
    partial void OnPhysicsMassChanged(double value) => PushTypedPropertyToBag("physics:mass", (float)value);
    partial void OnCoacdThresholdChanged(double value) => PushTypedPropertyToBag("physics:coacdThreshold", (float)value);
    partial void OnMaxConvexPiecesChanged(int value) => PushTypedPropertyToBag("physics:maxConvexPieces", value);
    partial void OnMaxHullVerticesChanged(int value) => PushTypedPropertyToBag("physics:maxHullVertices", value);
    partial void OnSelectedMaxTextureSizeChanged(DisplayOption value) => PushTypedPropertyToBag("material:maxTextureSize", value?.Value);
}
