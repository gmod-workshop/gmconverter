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
        var sourceOutput = Path.GetFullPath($"../../../../GMConverter.SourceEngine/bin/{configuration}/net10.0", AppContext.BaseDirectory);
        var sourceDirectory = Path.Join(root, "source");
        Directory.CreateDirectory(sourceDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceOutput))
        {
            File.Copy(file, Path.Join(sourceDirectory, Path.GetFileName(file)));
        }
        var unrealOutput = Path.GetFullPath($"../../../../GMConverter.UnrealEngine/bin/{configuration}/net10.0", AppContext.BaseDirectory);
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

    [AvaloniaFact]
    public void SourceAliasesShareStateAndLegacyEditsReachInactiveExporter()
    {
        var vm = CreateViewModel();
        Assert.Equal("mdl", vm.CurrentExporterOptions.ExporterFormat);
        Assert.Equal("1024", vm.CurrentExporterOptions.BuildExportOptions().GetString("material:maxTextureSize"));
        Assert.True(vm.CurrentExporterOptions.BuildExportOptions().GetBool("material:deduplicateTextures"));
        FindOption(vm, "physics:enabled").TryLoad(true);
        FindOption(vm, "physics:mode").TryLoad("coacd");
        FindOption(vm, "physics:mass").TryLoad(250f);
        Assert.True(vm.GeneratePhysics);
        Assert.Equal("coacd", vm.SelectedPhysicsMode.Value);
        Assert.Equal(250, vm.PhysicsMass);
        var sourceOptions = vm.CurrentExporterOptions;
        SelectFormat(vm, "glb");
        vm.PhysicsMass = 75;
        SelectFormat(vm, "source");
        Assert.Same(sourceOptions, vm.CurrentExporterOptions);
        Assert.Equal(75f, vm.CaptureSettings().ExporterOptions.GetFloat("physics:mass"));
        vm.GeneratePhysics = false;
        Assert.False(vm.CaptureSettings().ExporterOptions.GetBool("physics:enabled"));
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
        Assert.Equal(100, vm.PhysicsMass);
        var snapshot = vm.SnapshotExporterOptions();
        snapshot["unavailable-plugin"] = new() { ["preserved"] = "value" };
        var persisted = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object?>>>(JsonSerializer.Serialize(snapshot));
        var restored = CreateViewModel();
        // Exercise the same settings-loading path used on application startup.
        var settings = JsonSerializer.Deserialize<UiSettings>("{\"ScaleFactor\":1,\"PhysicsMass\":100,\"CoacdThreshold\":0.05,\"MaxConvexPieces\":16,\"MaxHullVertices\":16}")!;
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
        var panel = new ExporterOptionsPanel { DataContext = vm.CurrentExporterOptions };
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
        var vm = new ExporterOptionsViewModel("sample", new SchemaTestExporter().OptionSchema);
        vm.LoadFrom(new Dictionary<string, object?>
        {
            ["enabled"] = "invalid",
            ["count"] = long.MaxValue,
            ["physics:mass"] = "NaN",
            ["mode"] = "unknown",
            ["name"] = 12
        });
        var options = vm.BuildExportOptions();
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
