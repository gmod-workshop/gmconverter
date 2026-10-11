using GMConverter.UnrealEngine.Formats.Unreal;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UsmapLocatorTests : IDisposable
{
    private readonly string _installRoot = Path.Combine(Path.GetTempPath(), "gmconverter-usmap-" + Guid.NewGuid().ToString("N"));
    private readonly string _paksDirectory;

    public UsmapLocatorTests()
    {
        _paksDirectory = Path.Combine(_installRoot, "Game", "Content", "Paks");
        Directory.CreateDirectory(_paksDirectory);
    }

    public void Dispose()
    {
        Directory.Delete(_installRoot, recursive: true);
    }

    [Fact]
    public void ReturnsNullWhenNoMappingsArePresent()
    {
        Assert.Null(UsmapLocator.Find(_paksDirectory));
    }

    [Fact]
    public void FindsMappingsInTheInstallRoot()
    {
        var mappings = WriteMappings(_installRoot, "Game.usmap", DateTime.UtcNow);

        Assert.Equal(mappings, UsmapLocator.Find(_paksDirectory));
    }

    [Fact]
    public void PrefersMappingsClosestToTheArchives()
    {
        WriteMappings(_installRoot, "Root.usmap", DateTime.UtcNow);
        var paksMappings = WriteMappings(_paksDirectory, "Paks.usmap", DateTime.UtcNow.AddDays(-1));

        Assert.Equal(paksMappings, UsmapLocator.Find(_paksDirectory));
    }

    [Fact]
    public void PrefersTheNewestMappingsInADirectory()
    {
        WriteMappings(_paksDirectory, "Old.usmap", DateTime.UtcNow.AddDays(-7));
        var newest = WriteMappings(_paksDirectory, "New.usmap", DateTime.UtcNow);

        Assert.Equal(newest, UsmapLocator.Find(_paksDirectory));
    }

    [Fact]
    public void IgnoresMappingsAboveTheInstallRoot()
    {
        var nestedPaks = Path.Combine(_installRoot, "Game", "Content", "Paks", "Extra");
        Directory.CreateDirectory(nestedPaks);
        WriteMappings(_installRoot, "TooFar.usmap", DateTime.UtcNow);

        Assert.Null(UsmapLocator.Find(nestedPaks));
    }

    private static string WriteMappings(string directory, string fileName, DateTime lastWriteUtc)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, [0xC4, 0x30]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }
}
