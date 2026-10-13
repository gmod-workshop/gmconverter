using GMConverter.Core.Explorer;
using GMConverter.Core.Games;
using GMConverter.SDK.Common;
using GMConverter.SDK.Explorer;
using GMConverter.SDK.Games;

namespace GMConverter.UI.Tests;

public sealed class GameDetectionTests
{
    public GameDetectionTests()
    {
        ExporterOptionsTests.InitializePlugins();
    }

    [Fact]
    public void PluginGamesAreListedAndFoundById()
    {
        Assert.Contains(GameCatalog.Profiles, profile => profile.Id == "test.game");
        Assert.Contains(GameCatalog.Profiles, profile => profile.Id == "swzc");
        Assert.Equal("Test game", GameCatalog.Find("TEST.GAME")?.DisplayName);
        Assert.Null(GameCatalog.Find("missing"));
    }

    [Fact]
    public void DetectReturnsTheGameThatClaimsTheFolder()
    {
        var folder = CreateFolder(withMarker: true);

        var detection = GameCatalog.Detect(new ExplorerTarget(folder));

        Assert.NotNull(detection);
        Assert.Equal("test.game", detection.Profile.Id);
        Assert.Equal(GameMatch.Exact, detection.Match);
        Assert.True(detection.IsDetected);
    }

    [Fact]
    public void DetectReturnsNullWhenNoGameClaimsTheFolder()
    {
        Assert.Null(GameCatalog.Detect(new ExplorerTarget(CreateFolder(withMarker: false))));
    }

    [Fact]
    public void ResolveUsesAChosenGameWithoutDetecting()
    {
        var resolved = GameCatalog.Resolve(new ExplorerTarget(CreateFolder(withMarker: false)), "test.game");

        Assert.NotNull(resolved);
        Assert.Equal("test.game", resolved.Profile.Id);
        Assert.False(resolved.IsDetected);
        Assert.Throws<GMConverterException>(() => GameCatalog.Resolve(new ExplorerTarget(CreateFolder(withMarker: false)), "missing"));
    }

    [Fact]
    public void ScanReportsTheDetectedGameAndStampsItOnEveryEntry()
    {
        var folder = CreateFolder(withMarker: true);
        File.WriteAllText(Path.Join(folder, "model.psk"), "not read by the scan");

        var result = new ExplorerService().Scan(folder, "generic");

        Assert.Equal("test.game", result.Game?.Profile.Id);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("test.game", entry.GameId);
    }

    [Fact]
    public void ScanWithoutAGameLeavesEntriesUnstamped()
    {
        var folder = CreateFolder(withMarker: false);
        File.WriteAllText(Path.Join(folder, "model.psk"), "not read by the scan");

        var result = new ExplorerService().Scan(folder, "generic");

        Assert.Null(result.Game);
        Assert.Null(Assert.Single(result.Entries).GameId);
    }

    private static string CreateFolder(bool withMarker)
    {
        var folder = Path.Join(Path.GetTempPath(), "GMConverter.GameTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        if (withMarker)
        {
            File.WriteAllText(Path.Join(folder, SchemaTestGameProfile.MarkerFile), string.Empty);
        }
        return folder;
    }
}
