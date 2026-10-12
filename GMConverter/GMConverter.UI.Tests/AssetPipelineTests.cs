using System.Text.Json;
using GMConverter.Core.Pipeline;
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
        var source = AssetFormats.GetExport("source");
        var mdl = AssetFormats.GetExport("mdl");

        Assert.Same(mdl.Exporter, source.Exporter);
        Assert.Equal("mdl", source.OptionsKey);
        Assert.Single(AssetFormats.DistinctExporters, format => format.Exporter == mdl.Exporter);
    }

    [Fact]
    public void GltfFormatsHideTheOptionTheyForce()
    {
        Assert.DoesNotContain(AssetFormats.GetExport("glb").OptionSchema.AllOptions, option => option.Key == "binary");
        Assert.Contains(AssetFormats.GetExport("glb").Exporter.OptionSchema.AllOptions, option => option.Key == "binary");
        Assert.Equal("gltf", AssetFormats.GetExport("gltf").OptionsKey);
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
