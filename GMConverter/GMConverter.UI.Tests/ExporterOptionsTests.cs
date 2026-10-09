using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GMConverter.Plugins;
using GMConverter.UI.Controls.Settings;
using GMConverter.UI.Services;
using GMConverter.UI.ViewModels;
using GMConverter.UI.ViewModels.Options;

[assembly: AvaloniaTestApplication(typeof(GMConverter.UI.Tests.TestAppBuilder))]

namespace GMConverter.UI.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class ExporterOptionsTests
{
    private static readonly Lazy<string> _pluginsDirectory = new(StagePlugins);

    private static string StagePlugins()
    {
        var root = Path.Join(Path.GetTempPath(), "GMConverter.SchemaTests", Guid.NewGuid().ToString("N"));
        var testDirectory = Path.Join(root, "test");
        Directory.CreateDirectory(testDirectory);
        var assemblyPath = typeof(SchemaTestPlugin).Assembly.Location;
        File.Copy(assemblyPath, Path.Join(testDirectory, Path.GetFileName(assemblyPath)));
        File.WriteAllText(Path.Join(testDirectory, "plugin.json"), JsonSerializer.Serialize(new
        {
            id = "test.schema",
            version = "1.0",
            displayName = "Test",
            entry = Path.GetFileName(assemblyPath),
            sdkVersion = "1.0"
        }));
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var sourceOutput = Path.GetFullPath($"../../../../../Plugins/GMConverter.SourceEngine/bin/{configuration}/net10.0", AppContext.BaseDirectory);
        var sourceDirectory = Path.Join(root, "source");
        Directory.CreateDirectory(sourceDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceOutput))
        {
            File.Copy(file, Path.Join(sourceDirectory, Path.GetFileName(file)));
        }
        var unrealOutput = Path.GetFullPath($"../../../../../Plugins/GMConverter.UnrealEngine/bin/{configuration}/net10.0", AppContext.BaseDirectory);
        var unrealDirectory = Path.Join(root, "unreal");
        foreach (var file in Directory.EnumerateFiles(unrealOutput, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Join(unrealDirectory, Path.GetRelativePath(unrealOutput, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        return root;
    }

    internal static ConvertViewModel CreateViewModel()
    {
        PluginHost.Initialize(_pluginsDirectory.Value);
        var sink = new UiLogSink();
        return new ConvertViewModel(sink, new ConversionService(sink), () => false, _ => { }, _ => { }, (_, _) => { });
    }

    private static void SelectFormat(ConvertViewModel vm, string format)
    {
        vm.SelectedOutputFormat = vm.OutputFormats.Single(option => option.Value == format);
    }

    private static OptionViewModel FindOption(ConvertViewModel vm, string key) =>
        vm.CurrentExporterOptions.Groups.SelectMany(group => group.Options).Single(option => option.Key == key);

    private const string _minimalSettingsJson = "{\"ScaleFactor\":1}";

    [AvaloniaFact]
    public void SourceAliasSharesMdlOptionsAndOnlyChangedValuesPersist()
    {
        var vm = CreateViewModel();
        Assert.Equal("mdl", vm.CurrentExporterOptions.Format);
        var sourceOptions = vm.CurrentExporterOptions;
        FindOption(vm, "physics:mass").TryLoad(75f);
        SelectFormat(vm, "glb");
        SelectFormat(vm, "source");
        Assert.Same(sourceOptions, vm.CurrentExporterOptions);
        Assert.Equal(75f, vm.CaptureSettings().ExporterOptions.GetFloat("physics:mass"));

        // Only the edited value is saved; discovered tool paths and other defaults are not.
        Assert.Equal(["physics:mass"], vm.SnapshotExporterOptions()["mdl"].Keys);
        FindOption(vm, "physics:mass").TryLoad(100f);
        Assert.Empty(vm.SnapshotExporterOptions()["mdl"]);
    }

    [AvaloniaFact]
    public void LegacySourceSettingsMigrateIntoMdlExporterOptions()
    {
        var settings = UiSettings.Parse("""
            {
              "ScaleFactor": 1,
              "ModelPath": "gmconverter/old.mdl",
              "StudioMdlPath": "C:/tools/studiomdl.exe",
              "BuildMaterials": false,
              "GeneratePhysics": true,
              "PhysicsMode": "coacd",
              "PhysicsMass": 250,
              "CoacdThreshold": 0.01,
              "MaxConvexPieces": 32,
              "MaxHullVertices": 32,
              "MaxTextureSize": 1024,
              "DeduplicateTextures": true,
              "ExporterOptions": { "mdl": { "physics:mass": 300 } }
            }
            """)!;
        var vm = CreateViewModel();
        vm.ApplySettings(settings);
        SelectFormat(vm, "mdl");
        var options = vm.CaptureSettings().ExporterOptions;
        Assert.Equal("C:/tools/studiomdl.exe", options.GetString("studioMdlPath"));
        Assert.False(options.GetBool("buildMaterials", defaultValue: true));
        Assert.True(options.GetBool("physics:enabled"));
        Assert.Equal("coacd", options.GetString("physics:mode"));
        Assert.Equal("1024", options.GetString("material:maxTextureSize"));
        Assert.True(options.GetBool("material:deduplicateTextures"));
        // Values already saved the new way win, and untouched old CoACD defaults are replaced.
        Assert.Equal(300f, options.GetFloat("physics:mass"));
        Assert.Equal(16, options.GetInt("physics:maxConvexPieces"));
        Assert.Equal(0.05f, options.GetFloat("physics:coacdThreshold"));
        // The model path was per model; the exporter derives it again when it is blank.
        Assert.True(string.IsNullOrEmpty(options.GetString("modelPath")));
    }

    [AvaloniaFact]
    public void PreviewDrawsCollisionOnlyWhenTheExporterWouldGenerateIt()
    {
        var vm = CreateViewModel();
        var directory = Path.Join(Path.GetTempPath(), "GMConverter.SchemaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        vm.InputPath = Path.Join(directory, "input.sample");
        File.WriteAllText(vm.InputPath, "test");
        vm.SelectedInputFormat = vm.InputFormats.Single(option => option.Value == "sample");
        SelectFormat(vm, "mdl");
        var service = new ConversionService(new UiLogSink());

        Assert.Null(service.LoadPreview(vm.CaptureSettings()).PhysicsModelPath);
        FindOption(vm, "physics:enabled").TryLoad(true);
        var preview = service.LoadPreview(vm.CaptureSettings());
        Assert.True(File.Exists(preview.PhysicsModelPath));
        SelectFormat(vm, "glb");
        Assert.Null(service.LoadPreview(vm.CaptureSettings()).PhysicsModelPath);
    }

    [AvaloniaFact]
    public void PluginOptionsRoundTripJsonAndReachExporterWithoutHostProperties()
    {
        var vm = CreateViewModel();
        Assert.Contains(vm.InputFormats, option => option.Value == "sample");
        SelectFormat(vm, "sample");
        FindOption(vm, "name").TryLoad("custom");
        FindOption(vm, "path").TryLoad("example/path");
        FindOption(vm, "enabled").TryLoad(false);
        FindOption(vm, "count").TryLoad(42);
        FindOption(vm, "physics:mass").TryLoad(12.5f);
        FindOption(vm, "mode").TryLoad("b");
        var snapshot = vm.SnapshotExporterOptions();
        snapshot["unavailable-plugin"] = new() { ["preserved"] = "value" };
        var persisted = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object?>>>(JsonSerializer.Serialize(snapshot));
        var restored = CreateViewModel();
        // Exercise the same settings-loading path used on application startup.
        var settings = UiSettings.Parse(_minimalSettingsJson)!;
        restored.ApplySettings(settings with { ExporterOptions = persisted });
        SelectFormat(restored, "sample");
        Assert.Equal(42, restored.CaptureSettings().ExporterOptions.GetInt("count"));
        Assert.Equal(12.5f, restored.CaptureSettings().ExporterOptions.GetFloat("physics:mass"));
        Assert.Equal("b", restored.CaptureSettings().ExporterOptions.GetString("mode"));
        Assert.True(restored.SnapshotExporterOptions().ContainsKey("unavailable-plugin"));
        var output = Path.Join(Path.GetTempPath(), "GMConverter.SchemaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        restored.InputPath = Path.Join(output, "input.sample");
        File.WriteAllText(restored.InputPath, "test");
        restored.OutputPath = output;
        restored.BaseName = "result";
        restored.SelectedInputFormat = restored.InputFormats.Single(option => option.Value == "sample");
        new ConversionService(new UiLogSink()).RunConversion(restored.CaptureSettings());
        using var result = JsonDocument.Parse(File.ReadAllText(Path.Join(output, "result.json")));
        Assert.Equal("custom", result.RootElement.GetProperty("name").GetString());
        Assert.Equal("example/path", result.RootElement.GetProperty("path").GetString());
        Assert.False(result.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(42, result.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(12.5, result.RootElement.GetProperty("physics:mass").GetDouble());
    }

    [AvaloniaFact]
    public void PanelRendersEveryOptionTypeAndFiltersGroups()
    {
        var vm = CreateViewModel();
        SelectFormat(vm, "sample");
        var panel = new OptionsPanel { DataContext = vm.CurrentExporterOptions };
        var window = new Window { Content = panel, Width = 600, Height = 800 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var controls = panel.GetVisualDescendants().ToArray();
            Assert.Equal(2, controls.OfType<TextBox>().Count(control => control.DataContext is StringOptionViewModel or PathOptionViewModel));
            Assert.Single(controls.OfType<CheckBox>());
            Assert.Equal(2, controls.OfType<NumericUpDown>().Count());
            Assert.Single(controls.OfType<ComboBox>());
            controls.OfType<TextBox>().Single(control => control.DataContext is StringOptionViewModel).Text = "edited";
            controls.OfType<CheckBox>().Single().IsChecked = false;
            controls.OfType<NumericUpDown>().Single(control => control.DataContext is IntOptionViewModel).Value = 11;
            controls.OfType<NumericUpDown>().Single(control => control.DataContext is FloatOptionViewModel).Value = 19.25m;
            controls.OfType<ComboBox>().Single().SelectedItem = "b";
            Dispatcher.UIThread.RunJobs();
            var options = vm.CaptureSettings().ExporterOptions;
            Assert.Equal("edited", options.GetString("name"));
            Assert.False(options.GetBool("enabled"));
            Assert.Equal(11, options.GetInt("count"));
            Assert.Equal(19.25f, options.GetFloat("physics:mass"));
            Assert.Equal("b", options.GetString("mode"));
            panel.Groups = "unknown";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(panel.FilteredGroups);
            panel.Groups = "custom";
            Assert.Single(panel.FilteredGroups);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void InvalidPersistedValuesKeepDefaults()
    {
        var vm = new OptionSetViewModel("sample", new SchemaTestExporter().OptionSchema);
        vm.LoadFrom(new Dictionary<string, object?>
        {
            ["enabled"] = "invalid",
            ["count"] = long.MaxValue,
            ["physics:mass"] = "NaN",
            ["mode"] = "unknown",
            ["name"] = 12
        });
        var options = vm.BuildOptionValues();
        Assert.True(options.GetBool("enabled"));
        Assert.Equal(3, options.GetInt("count"));
        Assert.Equal(2.5f, options.GetFloat("physics:mass"));
        Assert.Equal("a", options.GetString("mode"));
        Assert.Equal("default", options.GetString("name"));
    }

    [AvaloniaFact]
    public void CliDiscoversSchemaArgumentsAndConvertsThroughPlugin()
    {
        _ = CreateViewModel();
        var output = Path.Join(Path.GetTempPath(), "GMConverter.SchemaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var input = Path.Join(output, "input.sample");
        File.WriteAllText(input, "test");
        var arguments = new[]
        {
            "--input-format", "sample", "--output-format", "sample", "--input-path", input,
            "--output-path", output, "--name", "cli", "--sample-count", "9", "--sample-mode", "b",
            "--sample-enabled", "false", "--sample-physics-mass", "17.5"
        };
        Assert.Equal(0, CLI.Program.Main(arguments));
        using var result = JsonDocument.Parse(File.ReadAllText(Path.Join(output, "cli.json")));
        Assert.Equal(9, result.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("b", result.RootElement.GetProperty("mode").GetString());
        Assert.False(result.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(17.5, result.RootElement.GetProperty("physics:mass").GetDouble());
        Assert.Equal("default", result.RootElement.GetProperty("name").GetString());
        Assert.Equal(2, CLI.Program.Main([.. arguments.Select(value => value == "9" ? "invalid" : value)]));
    }

    [AvaloniaFact]
    public void CliBooleanFlagsWorkBareAndNegatedAndEnumsIgnoreCase()
    {
        _ = CreateViewModel();
        var output = Path.Join(Path.GetTempPath(), "GMConverter.SchemaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var input = Path.Join(output, "input.sample");
        File.WriteAllText(input, "test");
        string[] common = ["--input-format", "sample", "--output-format", "sample", "--input-path", input, "--output-path", output];

        Assert.Equal(0, CLI.Program.Main([.. common, "--name", "bare", "--sample-enabled", "--sample-mode", "B"]));
        Assert.Equal(0, CLI.Program.Main([.. common, "--name", "negated", "--no-sample-enabled"]));

        JsonElement Result(string name) => JsonDocument.Parse(File.ReadAllText(Path.Join(output, name + ".json"))).RootElement;
        Assert.True(Result("bare").GetProperty("enabled").GetBoolean());
        Assert.Equal("b", Result("bare").GetProperty("mode").GetString());
        Assert.False(Result("negated").GetProperty("enabled").GetBoolean());
    }

    [AvaloniaFact]
    public void CliImporterArgumentsAndAliasesReachImporter()
    {
        _ = CreateViewModel();
        var output = Path.Join(Path.GetTempPath(), "GMConverter.SchemaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var input = Path.Join(output, "input.sample");
        File.WriteAllText(input, "test");
        string[] common = ["--input-format", "sample", "--output-format", "sample", "--input-path", input, "--output-path", output];

        Assert.Equal(0, CLI.Program.Main([.. common, "--name", "flag", "--sample-import-modelName", "from-flag"]));
        Assert.Equal(0, CLI.Program.Main([.. common, "--name", "alias", "--sample-model-name", "from-alias"]));
        Assert.Equal(0, CLI.Program.Main([.. common, "--name", "default"]));

        string ModelName(string name) => JsonDocument.Parse(File.ReadAllText(Path.Join(output, name + ".json"))).RootElement.GetProperty("model").GetString()!;
        Assert.Equal("from-flag", ModelName("flag"));
        Assert.Equal("from-alias", ModelName("alias"));
        Assert.Equal("triangle", ModelName("default"));

        // Input files are checked against the importer's declared extensions.
        var wrongExtension = Path.ChangeExtension(input, ".txt");
        File.WriteAllText(wrongExtension, "test");
        Assert.Equal(1, CLI.Program.Main([.. common[..4], wrongExtension, .. common[6..]]));
    }

    [AvaloniaFact]
    public void ImporterOptionsFollowInputFormatPersistAndAcceptExplorerAndConfigValues()
    {
        var vm = CreateViewModel();
        vm.SelectedInputFormat = vm.InputFormats.Single(option => option.Value == "sample");
        Assert.Equal("sample", vm.CurrentImporterOptions.Format);
        vm.CurrentImporterOptions.Groups.SelectMany(group => group.Options).Single().TryLoad("edited");
        Assert.Equal("edited", vm.CaptureSettings().ImporterOptions.GetString("modelName"));

        var persisted = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object?>>>(JsonSerializer.Serialize(vm.SnapshotImporterOptions()));
        var restored = CreateViewModel();
        var settings = UiSettings.Parse(_minimalSettingsJson)!;
        restored.ApplySettings(settings with { ImporterOptions = persisted });
        restored.SelectedInputFormat = restored.InputFormats.Single(option => option.Value == "sample");
        Assert.Equal("edited", restored.CaptureSettings().ImporterOptions.GetString("modelName"));

        // An explorer selection starts from defaults plus the values the explorer resolved.
        restored.ApplyExplorerSelection(
            new SDK.Explorer.ExplorerFileEntry("entry", "entry.sample", "sample", "", ""),
            new SDK.Explorer.ExplorerResolvedEntry("entry.sample", "", ImporterOptions: new Dictionary<string, object?> { ["modelName"] = "explored" }));
        Assert.Equal("explored", restored.CaptureSettings().ImporterOptions.GetString("modelName"));
        restored.ApplyExplorerSelection(new SDK.Explorer.ExplorerFileEntry("other", "other.sample", "sample", "", ""));
        Assert.Equal("triangle", restored.CaptureSettings().ImporterOptions.GetString("modelName"));

        // Config files reach importer options by key or alias, case- and separator-insensitively.
        var config = Path.Join(Path.GetTempPath(), "GMConverter.SchemaTests", Guid.NewGuid().ToString("N"), "test.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, "Sample_Model_Name = from-config" + Environment.NewLine);
        restored.ConfigPath = config;
        restored.LoadConfigCommand.Execute(null);
        Assert.Equal("from-config", restored.CaptureSettings().ImporterOptions.GetString("modelName"));
    }

    [AvaloniaTheory]
    [InlineData("glb")]
    [InlineData("gltf")]
    public void GltfFormatControlsOutputExtension(string format)
    {
        var vm = CreateViewModel();
        SelectFormat(vm, format);
        Assert.DoesNotContain(vm.CurrentExporterOptions.Schema.AllOptions, option => option.Key == "binary");
        var output = Path.Join(Path.GetTempPath(), "GMConverter.SchemaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        vm.InputPath = Path.Join(output, "input.sample");
        File.WriteAllText(vm.InputPath, "test");
        vm.OutputPath = output;
        vm.BaseName = "gltf";
        vm.SelectedInputFormat = vm.InputFormats.Single(option => option.Value == "sample");
        new ConversionService(new UiLogSink()).RunConversion(vm.CaptureSettings());
        Assert.True(File.Exists(Path.Join(output, "gltf." + format)));
    }
}
