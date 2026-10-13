using GMConverter.SDK.Explorer;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Profiles;


namespace GMConverter.UI.Tests;

/// <summary>A game claimed by any folder holding a <see cref="MarkerFile"/>; it marks the models it prepares.</summary>
public sealed class SchemaTestGameProfile : IImportGameProfile
{
    public const string MarkerFile = "test-game.marker";

    public string Id => "test.game";
    public string DisplayName => "Test game";
    public string Engine => "Test engine";

    public GameMatch Detect(ExplorerTarget target) =>
        target.IsDirectory && File.Exists(Path.Join(target.FullPath, MarkerFile)) ? GameMatch.Exact : GameMatch.None;

    public Model Prepare(Model model) => model with { Name = model.Name + "-from-test-game" };
}
