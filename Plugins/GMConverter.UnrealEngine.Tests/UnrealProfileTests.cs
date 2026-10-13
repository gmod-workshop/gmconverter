using GMConverter.SDK.Explorer;
using GMConverter.SDK.Profiles;
using GMConverter.UnrealEngine.Profiles;
using GMConverter.UnrealEngine.Profiles.Fortnite;
using GMConverter.UnrealEngine.Profiles.ZeroCompany;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UnrealProfileTests
{
    [Fact]
    public void ZeroCompanyIsExactWhenItsExecutableSitsAboveThePaks()
    {
        var install = CreateFolder("Star Wars Zero Company");
        File.WriteAllText(Path.Join(install, "SWZeroCompany.exe"), string.Empty);
        var paks = CreatePaks(Path.Join(install, "SWZeroCompany", "Content", "Paks"));

        Assert.Equal(GameMatch.Exact, new ZeroCompanyProfile().Detect(new ExplorerTarget(paks)));
        // The project folder resolves to its Content/Paks, and the install folder holds the
        // project, so both are detected too.
        Assert.Equal(GameMatch.Exact, new ZeroCompanyProfile().Detect(new ExplorerTarget(Path.Join(install, "SWZeroCompany"))));
        Assert.Equal(GameMatch.Exact, new ZeroCompanyProfile().Detect(new ExplorerTarget(install)));
    }

    [Fact]
    public void ZeroCompanyIsLikelyFromTheProjectFolderNameAlone()
    {
        var paks = CreatePaks(Path.Join(CreateFolder("copy"), "SWZeroCompany", "Content", "Paks"));

        Assert.Equal(GameMatch.Likely, new ZeroCompanyProfile().Detect(new ExplorerTarget(paks)));
    }

    [Fact]
    public void GenericClaimsAnyArchiveFolderAsAFallbackOnly()
    {
        var paks = CreatePaks(Path.Join(CreateFolder("SomeGame"), "Paks"));
        var empty = CreateFolder("empty");

        Assert.Equal(GameMatch.Fallback, new GenericUnrealProfile().Detect(new ExplorerTarget(paks)));
        Assert.Equal(GameMatch.None, new GenericUnrealProfile().Detect(new ExplorerTarget(empty)));
        Assert.Equal(GameMatch.None, new ZeroCompanyProfile().Detect(new ExplorerTarget(paks)));
        Assert.Equal(GameMatch.None, new FortniteProfile().Detect(new ExplorerTarget(paks)));
    }

    [Fact]
    public void SelectPrefersTheStrongestDetection()
    {
        var install = CreateFolder("install");
        File.WriteAllText(Path.Join(install, "SWZeroCompany.exe"), string.Empty);
        var zeroCompanyPaks = CreatePaks(Path.Join(install, "SWZeroCompany", "Content", "Paks"));
        var otherPaks = CreatePaks(Path.Join(CreateFolder("OtherGame"), "Paks"));

        Assert.Equal("swzc", UnrealProfiles.Select(zeroCompanyPaks, gameId: null).Id);
        Assert.Equal("unreal", UnrealProfiles.Select(otherPaks, gameId: null).Id);
    }

    [Fact]
    public void SelectUsesAChosenGameAndIgnoresOtherPluginsGames()
    {
        var otherPaks = CreatePaks(Path.Join(CreateFolder("OtherGame"), "Paks"));

        Assert.Equal("swzc", UnrealProfiles.Select(otherPaks, "SWZC").Id);
        Assert.Equal("unreal", UnrealProfiles.Select(otherPaks, "garrysmod").Id);
    }

    [Fact]
    public void EveryProfileHasAUniqueIdAndAnEngine()
    {
        Assert.Equal(UnrealProfiles.All.Count, UnrealProfiles.All.Select(profile => profile.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(UnrealProfiles.All, profile => Assert.False(string.IsNullOrWhiteSpace(profile.Engine)));
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
