using GMConverter.SDK.Explorer;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Options;
using GMConverter.SDK.Profiles;


namespace GMConverter.UI.Tests;

/// <summary>A target of the sample exporter that appends its <c>suffix</c> option (empty by default) to the model's name.</summary>
public sealed class SchemaTestTargetProfile(string id, string displayName) : IExportGameProfile
{
    public string Id => id;
    public string DisplayName => displayName;
    public string Engine => "Test engine";
    public string ExporterFormat => "sample";
    public OptionSchema OptionSchema { get; } = new([
        new OptionGroup("target", "Target", [
            new OptionDescriptor("suffix", OptionType.String, "Suffix") { DefaultValue = "" }
        ])
    ]);

    public GameMatch Detect(ExplorerTarget target) => GameMatch.None;

    public Model Prepare(Model model, OptionValues options) => model with { Name = model.Name + options.GetString("suffix") };
}
