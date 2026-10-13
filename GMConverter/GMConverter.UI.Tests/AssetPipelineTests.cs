using System.Text.Json;
using GMConverter.Core.Formats;
using GMConverter.Core.Pipeline;
using GMConverter.SDK.Assets;
using GMConverter.SDK.Common;
using GMConverter.SDK.Options;

namespace GMConverter.UI.Tests;

public sealed class AssetPipelineTests
{
    public AssetPipelineTests()
    {
        ExporterOptionsTests.InitializePlugins();
    }

    [Fact]
    public void RunFillsSchemaDefaultsTheRequestLeftUnset()
    {
        var output = CreateOutputDirectory();
        var request = new AssetRequest("sample", CreateInput(output))
        {
            OutputFormat = "sample",
            OutputDirectory = output,
            BaseName = "result",
            ImporterOptions = new OptionValues(new Dictionary<string, object?> { ["modelName"] = "named" }),
            ExporterOptions = new OptionValues(new Dictionary<string, object?> { ["count"] = 7 }),
        };

        var result = new AssetJobRunner().Run(new AssetJob(request)).Results.Single();

        Assert.Equal(Path.GetFullPath(output), result.Destination.Directory);
        using var written = JsonDocument.Parse(File.ReadAllText(Path.Join(output, "result.json")));
        Assert.Equal(7, written.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("default", written.RootElement.GetProperty("name").GetString());
        Assert.Equal("a", written.RootElement.GetProperty("mode").GetString());
        Assert.Equal("named", written.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public void ResultCarriesTheFilesTheExporterReported()
    {
        var output = CreateOutputDirectory();
        var request = new AssetRequest("sample", CreateInput(output))
        {
            OutputFormat = "sample",
            OutputDirectory = output,
            BaseName = "result",
        };

        var result = new AssetJobRunner().Run(new AssetJob(request)).Results.Single();

        Assert.Equal([Path.Join(Path.GetFullPath(output), "result.json")], result.Export.Files);
    }

    [Fact]
    public void ModelImportersAndExportersReportTheModelKind()
    {
        Assert.Equal(AssetKind.Model, FormatCatalog.GetImporter("sample").Kind);
        Assert.All(FormatCatalog.Exports, format => Assert.Equal(AssetKind.Model, format.Exporter.Kind));
    }

    [Fact]
    public void DefaultTargetPreparesTheModelAndReachesTheExporter()
    {
        var output = CreateOutputDirectory();
        var request = new AssetRequest("sample", CreateInput(output))
        {
            OutputFormat = "sample",
            OutputDirectory = output,
            BaseName = "result",
        };

        var result = new AssetJobRunner().Run(new AssetJob(request)).Results.Single();

        Assert.Equal("test.target", result.Destination.Target?.Id);
        using var written = JsonDocument.Parse(File.ReadAllText(Path.Join(output, "result.json")));
        Assert.Equal("test.target", written.RootElement.GetProperty("target").GetString());
        // The target's options arrive with their defaults filled in.
        Assert.Equal(string.Empty, written.RootElement.GetProperty("targetSuffix").GetString());
        Assert.Equal("triangle", written.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public void ChosenTargetAndItsOptionsAreUsed()
    {
        var output = CreateOutputDirectory();
        var request = new AssetRequest("sample", CreateInput(output))
        {
            OutputFormat = "sample",
            OutputDirectory = output,
            BaseName = "result",
            TargetGameId = "TEST.TARGET2",
            TargetOptions = new OptionValues(new Dictionary<string, object?> { ["suffix"] = "!" }),
        };

        new AssetJobRunner().Run(new AssetJob(request));

        using var written = JsonDocument.Parse(File.ReadAllText(Path.Join(output, "result.json")));
        Assert.Equal("test.target2", written.RootElement.GetProperty("target").GetString());
        Assert.Equal("triangle!", written.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public void UnknownTargetFailsBeforeTheInputIsRead()
    {
        var request = new AssetRequest("sample", Path.Join(CreateOutputDirectory(), "missing.sample"))
        {
            OutputFormat = "sample",
            OutputDirectory = CreateOutputDirectory(),
            TargetGameId = "garrysmod",
        };

        var error = Assert.Throws<GMConverterException>(() => new AssetJobRunner().Run(new AssetJob(request)));

        Assert.Contains("test.target, test.target2", error.Message);
    }

    [Fact]
    public void FormatsWithoutTargetsExportWithoutOne()
    {
        var output = CreateOutputDirectory();
        var request = new AssetRequest("sample", CreateInput(output))
        {
            OutputFormat = "obj",
            OutputDirectory = output,
        };

        var result = new AssetJobRunner().Run(new AssetJob(request)).Results.Single();

        Assert.Null(result.Destination.Target);
        Assert.Throws<GMConverterException>(() => new AssetJobRunner().Run(new AssetJob(request with { TargetGameId = "test.target" })));
    }

    [Fact]
    public void SourceGamePreparesTheImportedModel()
    {
        var output = CreateOutputDirectory();
        var request = new AssetRequest("sample", CreateInput(output))
        {
            OutputFormat = "sample",
            OutputDirectory = output,
            BaseName = "result",
            SourceGameId = "test.game",
        };

        new AssetJobRunner().Run(new AssetJob(request));

        using var written = JsonDocument.Parse(File.ReadAllText(Path.Join(output, "result.json")));
        Assert.Equal("triangle-from-test-game", written.RootElement.GetProperty("model").GetString());
        Assert.Throws<GMConverterException>(() => new AssetJobRunner().Run(new AssetJob(request with { SourceGameId = "test.target" })));
    }

    [Fact]
    public void BaseNameDefaultsToTheInputFileName()
    {
        var output = CreateOutputDirectory();
        var request = new AssetRequest("sample", CreateInput(output))
        {
            OutputFormat = "sample",
            OutputDirectory = output,
        };

        var result = new AssetJobRunner().Run(new AssetJob(request)).Results.Single();

        Assert.Equal("input", result.Destination.BaseName);
        Assert.True(File.Exists(Path.Join(output, "input.json")));
    }

    [Theory]
    [InlineData("glb")]
    [InlineData("gltf")]
    public void GltfFormatOverridesTheBinaryOption(string format)
    {
        var output = CreateOutputDirectory();
        var request = new AssetRequest("sample", CreateInput(output))
        {
            OutputFormat = format,
            OutputDirectory = output,
            BaseName = "model",
            // The format, not this option, decides binary or text output.
            ExporterOptions = new OptionValues(new Dictionary<string, object?> { ["binary"] = format == "gltf" }),
        };

        new AssetJobRunner().Run(new AssetJob(request));

        Assert.True(File.Exists(Path.Join(output, "model." + format)));
    }

    [Fact]
    public void SourceIsTheMdlExporterUnderAnotherName()
    {
        var source = FormatCatalog.GetExport("source");
        var mdl = FormatCatalog.GetExport("mdl");

        Assert.Same(mdl.Exporter, source.Exporter);
        Assert.Equal("mdl", source.OptionsKey);
        Assert.Single(FormatCatalog.DistinctExporters, format => format.Exporter == mdl.Exporter);
    }

    [Fact]
    public void GltfFormatsHideTheOptionTheyForce()
    {
        Assert.DoesNotContain(FormatCatalog.GetExport("glb").OptionSchema.AllOptions, option => option.Key == "binary");
        Assert.Contains(FormatCatalog.GetExport("glb").Exporter.OptionSchema.AllOptions, option => option.Key == "binary");
        Assert.Equal("gltf", FormatCatalog.GetExport("gltf").OptionsKey);
    }

    [Fact]
    public void MissingOutputPathFailsBeforeTheInputIsRead()
    {
        var request = new AssetRequest("sample", Path.Join(CreateOutputDirectory(), "missing.sample"))
        {
            OutputFormat = "sample",
        };

        var error = Assert.Throws<GMConverterException>(() => new AssetJobRunner().Run(new AssetJob(request)));

        Assert.Contains("Output path is required", error.Message);
    }

    [Fact]
    public void InputMustUseAnExtensionTheImporterReads()
    {
        var output = CreateOutputDirectory();
        var input = Path.Join(output, "input.txt");
        File.WriteAllText(input, "test");
        var request = new AssetRequest("sample", input)
        {
            OutputFormat = "sample",
            OutputDirectory = output,
        };

        var error = Assert.Throws<GMConverterException>(() => new AssetJobRunner().Run(new AssetJob(request)));

        Assert.Contains(".sample", error.Message);
    }

    private static string CreateOutputDirectory()
    {
        var output = Path.Join(Path.GetTempPath(), "GMConverter.PipelineTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        return output;
    }

    private static string CreateInput(string directory)
    {
        var input = Path.Join(directory, "input.sample");
        File.WriteAllText(input, "test");
        return input;
    }
}
