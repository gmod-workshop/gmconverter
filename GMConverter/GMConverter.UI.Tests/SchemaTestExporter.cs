using System.Text.Json;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Options;


namespace GMConverter.UI.Tests;

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
            JsonSerializer.Serialize(new Dictionary<string, object?>(context.Options.AsDictionary())
            {
                ["model"] = model.Name,
                ["target"] = context.TargetGameId,
                ["targetSuffix"] = context.TargetOptions.GetString("suffix"),
            }));
        return new ExportResult([path]);
    }
}
