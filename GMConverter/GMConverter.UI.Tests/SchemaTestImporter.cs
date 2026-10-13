using System.Numerics;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;


namespace GMConverter.UI.Tests;

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
