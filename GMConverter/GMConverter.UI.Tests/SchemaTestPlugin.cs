using System.Numerics;
using System.Text.Json;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
using GMConverter.SDK.Plugins;

[assembly: Plugin(typeof(GMConverter.UI.Tests.SchemaTestPlugin))]

namespace GMConverter.UI.Tests;

public sealed class SchemaTestPlugin : IPlugin
{
    public string Id => "test.schema";
    public string DisplayName => "Schema test";

    public void OnLoad(IPluginContext context)
    {
        context.RegisterImporter(new SchemaTestImporter());
        context.RegisterExporter(new SchemaTestExporter());
    }

    public void OnUnload()
    {
    }
}

public sealed class SchemaTestImporter : IModelImporter
{
    public string Format => "sample";
    public string DisplayName => "Test model";
    public IReadOnlyList<string> FileExtensions { get; } = [".sample"];
    public OptionSchema OptionSchema { get; } = new([
        new OptionGroup("naming", "Naming", [
            new OptionDescriptor("modelName", OptionType.String, "Model name")
            {
                DefaultValue = "triangle",
                Aliases = ["sample-model-name"],
            }
        ])
    ]);
    public object Summarize(string inputPath) => "Test model";

    public Model Import(ModelImportContext context) => new(context.Options.GetString("modelName") ?? "triangle",
    [
        new Mesh([
            new Vertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero),
            new Vertex(Vector3.UnitX, Vector3.UnitZ, Vector2.UnitX),
            new Vertex(Vector3.UnitY, Vector3.UnitZ, Vector2.UnitY)
        ], [new Submesh("test", [new Triangle(0, 1, 2)])])
    ], [new Material("test")]);
}

public sealed class SchemaTestExporter : IModelExporter
{
    public string Format => "sample";
    public string DisplayName => "Test export";
    public OptionSchema OptionSchema { get; } = new([
        new OptionGroup("custom", "Custom", [
            new OptionDescriptor("name", OptionType.String, "Name") { DefaultValue = "default" },
            new OptionDescriptor("path", OptionType.Path, "Path"),
            new OptionDescriptor("enabled", OptionType.Bool, "Enabled") { DefaultValue = true, Aliases = ["no-sample-enabled"] },
            new OptionDescriptor("count", OptionType.Int, "Count") { DefaultValue = 3 },
            new OptionDescriptor("physics:mass", OptionType.Float, "Amount") { DefaultValue = 2.5f },
            new OptionDescriptor("mode", OptionType.Enum, "Mode") { Choices = ["a", "b"], DefaultValue = "a" }
        ])
    ]);

    public ExportResult Export(Model model, ExportContext context)
    {
        // The model name echoes the importer's "modelName" option, so tests can see both sides.
        var path = Path.Join(context.OutputDirectory, context.BaseName + ".json");
        File.WriteAllText(path,
            JsonSerializer.Serialize(new Dictionary<string, object?>(context.Options.AsDictionary()) { ["model"] = model.Name }));
        return new ExportResult([path]);
    }
}
