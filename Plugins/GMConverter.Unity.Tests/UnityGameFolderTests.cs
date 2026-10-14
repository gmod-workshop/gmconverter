using GMConverter.SDK.Explorer;
using GMConverter.SDK.Profiles;
using GMConverter.Unity.Assets;
using GMConverter.Unity.Explorer;
using GMConverter.Unity.Profiles.LiarsBar;

namespace GMConverter.Unity.Tests;

public sealed class UnityGameFolderTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), $"gmc-unity-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void FindDataDirectoryAcceptsInstallFolderDataFolderAndFiles()
    {
        var dataDirectory = CreateBuild("Liar's Bar");
        var install = Path.GetDirectoryName(dataDirectory)!;

        Assert.Equal(dataDirectory, UnityGameFolder.FindDataDirectory(install));
        Assert.Equal(dataDirectory, UnityGameFolder.FindDataDirectory(dataDirectory));
        Assert.Equal(dataDirectory, UnityGameFolder.FindDataDirectory(Path.Join(dataDirectory, "level0")));
        Assert.Null(UnityGameFolder.FindDataDirectory(_root));
    }

    [Fact]
    public void GetAssetFileNamesListsScenesThenSharedAssetsThenResources()
    {
        var dataDirectory = CreateBuild("Game");
        foreach (var name in new[] { "resources.assets", "sharedassets10.assets", "sharedassets2.assets", "level10", "level2", "level2.resS", "sharedassets2.assets.resS", "boot.config" })
        {
            File.WriteAllText(Path.Join(dataDirectory, name), string.Empty);
        }

        Assert.Equal(
            ["level0", "level2", "level10", "sharedassets2.assets", "sharedassets10.assets", "resources.assets"],
            UnityGameFolder.GetAssetFileNames(dataDirectory));
    }

    [Fact]
    public void LiarsBarProfileDetectsItsDataFolderOnly()
    {
        var profile = new LiarsBarProfile();
        var liarsBar = CreateBuild("Liar's Bar");
        var other = CreateBuild("Other Game");

        Assert.Equal(GameMatch.Exact, profile.Detect(new ExplorerTarget(Path.GetDirectoryName(liarsBar)!)));
        Assert.Equal(GameMatch.None, profile.Detect(new ExplorerTarget(Path.GetDirectoryName(other)!)));
    }

    [Fact]
    public void ExplorerSupportsUnityBuildsOnly()
    {
        var explorer = new UnityExplorer();
        var dataDirectory = CreateBuild("Game");

        Assert.True(explorer.Supports(new ExplorerTarget(Path.GetDirectoryName(dataDirectory)!)));
        Assert.False(explorer.Supports(new ExplorerTarget(_root)));
    }

    [Fact]
    public void LibraryLoadsTheEmbeddedClassDatabase()
    {
        var dataDirectory = CreateBuild("Game");

        var library = UnityAssetLibrary.For(dataDirectory);

        Assert.Same(library, UnityAssetLibrary.For(dataDirectory));
        UnityAssetLibrary.ClearAll();
    }

    private string CreateBuild(string productName)
    {
        var dataDirectory = Path.Join(_root, productName, productName + "_Data");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Join(dataDirectory, UnityGameFolder.GameManagersFileName), string.Empty);
        File.WriteAllText(Path.Join(dataDirectory, "level0"), string.Empty);
        return dataDirectory;
    }
}
