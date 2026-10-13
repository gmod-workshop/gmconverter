using GMConverter.SDK.Explorer;
using GMConverter.SDK.Games;
using GMConverter.UnrealEngine.Formats.Unreal;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UnrealGameProfileTests
{
    [Fact]
    public void ZeroCompanyIsExactWhenItsExecutableSitsAboveThePaks()
    {
        var install = CreateFolder("Star Wars Zero Company");
        File.WriteAllText(Path.Join(install, "SWZeroCompany.exe"), string.Empty);
        var paks = CreatePaks(Path.Join(install, "SWZeroCompany", "Content", "Paks"));

        Assert.Equal(GameMatch.Exact, new ZeroCompanyUnrealGameProfile().Detect(new ExplorerTarget(paks)));
        // The project folder resolves to its Content/Paks, and the install folder holds the
        // project, so both are detected too.
        Assert.Equal(GameMatch.Exact, new ZeroCompanyUnrealGameProfile().Detect(new ExplorerTarget(Path.Join(install, "SWZeroCompany"))));
        Assert.Equal(GameMatch.Exact, new ZeroCompanyUnrealGameProfile().Detect(new ExplorerTarget(install)));
    }

    [Fact]
    public void ZeroCompanyIsLikelyFromTheProjectFolderNameAlone()
    {
        var paks = CreatePaks(Path.Join(CreateFolder("copy"), "SWZeroCompany", "Content", "Paks"));

        Assert.Equal(GameMatch.Likely, new ZeroCompanyUnrealGameProfile().Detect(new ExplorerTarget(paks)));
    }

    [Fact]
    public void GenericClaimsAnyArchiveFolderAsAFallbackOnly()
    {
        var paks = CreatePaks(Path.Join(CreateFolder("SomeGame"), "Paks"));
        var empty = CreateFolder("empty");

        Assert.Equal(GameMatch.Fallback, new GenericUnrealGameProfile().Detect(new ExplorerTarget(paks)));
        Assert.Equal(GameMatch.None, new GenericUnrealGameProfile().Detect(new ExplorerTarget(empty)));
        Assert.Equal(GameMatch.None, new ZeroCompanyUnrealGameProfile().Detect(new ExplorerTarget(paks)));
        Assert.Equal(GameMatch.None, new FortniteUnrealGameProfile().Detect(new ExplorerTarget(paks)));
    }

    [Fact]
    public void SelectPrefersTheStrongestDetection()
    {
        var install = CreateFolder("install");
        File.WriteAllText(Path.Join(install, "SWZeroCompany.exe"), string.Empty);
        var zeroCompanyPaks = CreatePaks(Path.Join(install, "SWZeroCompany", "Content", "Paks"));
        var otherPaks = CreatePaks(Path.Join(CreateFolder("OtherGame"), "Paks"));

        Assert.Equal("swzc", UnrealGameProfiles.Select(zeroCompanyPaks, gameId: null).Id);
        Assert.Equal("unreal", UnrealGameProfiles.Select(otherPaks, gameId: null).Id);
    }

    [Fact]
    public void SelectUsesAChosenGameAndIgnoresOtherPluginsGames()
    {
        var otherPaks = CreatePaks(Path.Join(CreateFolder("OtherGame"), "Paks"));

        Assert.Equal("swzc", UnrealGameProfiles.Select(otherPaks, "SWZC").Id);
        Assert.Equal("unreal", UnrealGameProfiles.Select(otherPaks, "garrysmod").Id);
    }

    [Fact]
    public void EveryProfileHasAUniqueIdAndAnEngine()
    {
        Assert.Equal(UnrealGameProfiles.All.Count, UnrealGameProfiles.All.Select(profile => profile.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(UnrealGameProfiles.All, profile => Assert.False(string.IsNullOrWhiteSpace(profile.Engine)));
    }

    private static string CreateFolder(string name)
    {
        var folder = Path.Join(Path.GetTempPath(), "GMConverter.UnrealGameTests", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string CreatePaks(string paks)
    {
        Directory.CreateDirectory(paks);
        File.WriteAllText(Path.Join(paks, "pakchunk0-Windows.pak"), string.Empty);
        return paks;
    }
}
