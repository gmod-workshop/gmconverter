using GMConverter.SDK.Audio;
using GMConverter.UnrealEngine.Audio;
using GMConverter.UnrealEngine.Formats.Wwise;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UnrealSoundNamesTests
{
    // Wwise events from Star Wars Zero Company.
    [Theory]
    [InlineData("FS_B1", "/Game/WwiseAudio/Characters/B1_Battledroid/FS_B1.FS_B1", SoundCategory.Footstep)]
    [InlineData("FLY_B1_Weapon_Attach", "/Game/WwiseAudio/Characters/B1_Battledroid/FLY_B1_Weapon_Attach.FLY_B1_Weapon_Attach", SoundCategory.Movement)]
    [InlineData("brk_combat_i_am_alone_b1_004665", "/Game/WwiseAudio/VO/Tactical/VO_Tactical_B1/B1_Tactical/brk_combat_i_am_alone_b1_004665.brk_combat_i_am_alone_b1_004665", SoundCategory.Voice)]
    [InlineData("WPN_E5_Fire", "/Game/WwiseAudio/Weapons/Blasters/WPN_E5_Fire.WPN_E5_Fire", SoundCategory.Weapon)]
    [InlineData("Play_Shot_E5", "/Game/WwiseAudio/Weapons/Blasters/Play_Shot_E5.Play_Shot_E5", SoundCategory.Weapon)]
    [InlineData("ENV_Den_Wind", "/Game/WwiseAudio/Environment/ENV_Den/ENV_Den_Wind.ENV_Den_Wind", SoundCategory.Ambient)]
    [InlineData("UI_Click", "/Game/WwiseAudio/UI/Global/UI_Click.UI_Click", SoundCategory.UI)]
    [InlineData("MX_Combat_Start", "/Game/WwiseAudio/Music/MX_Combat_Start.MX_Combat_Start", SoundCategory.Music)]
    [InlineData("Play_Something", "/Game/WwiseAudio/Misc/Play_Something.Play_Something", SoundCategory.Other)]
    public void CategoryComesFromThePrefixThenTheFolders(string name, string path, SoundCategory category)
    {
        Assert.Equal(category, UnrealSoundNames.Category(name, path));
    }

    [Theory]
    [InlineData("brk_combat_i_am_alone_b1_004665 (English(US))", "English(US)")]
    [InlineData("brk_combat_i_am_alone_b1_004665 (German)", "German")]
    [InlineData("FS_B1 (492522486)", null)]
    [InlineData("FLY_B1_AimStart (SFX)", null)]
    [InlineData("FS_B1", null)]
    public void LanguageIsTheTrailingNameUnlessItIsAMediaId(string mediaName, string? language)
    {
        Assert.Equal(language, UnrealSoundNames.Language(mediaName));
    }

    [Fact]
    public void EnglishIsPreferredOverOtherLanguages()
    {
        Assert.Equal("English(US)", UnrealSoundNames.PreferredLanguage(["German", null, "English(US)", "French(France)"]));
        Assert.Equal("English(UK)", UnrealSoundNames.PreferredLanguage(["German", "English(UK)"]));
        Assert.Equal("German", UnrealSoundNames.PreferredLanguage([null, "German"]));
        Assert.Null(UnrealSoundNames.PreferredLanguage([null, null]));
    }

    [Theory]
    [InlineData("FS_B1 (492522486)", "FS_B1_492522486")]
    [InlineData("brk_alone (English(US))", "brk_alone_English_US")]
    [InlineData("()", "sound")]
    public void ClipNamesAreFileFriendly(string mediaName, string clipName)
    {
        Assert.Equal(clipName, UnrealSoundNames.ClipName(mediaName));
    }

    [Fact]
    public void EventSidecarRoundTrips()
    {
        var directory = Path.Join(Path.GetTempPath(), "GMConverter.UnrealEngine.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Join(directory, "FS_B1" + WwiseEventSidecar.Extension);
        var sidecar = new WwiseEventSidecar("FS_B1", SoundCategory.Footstep, 25f, ["FS_B1_1.wem", "FS_B1_2.wem"]);

        sidecar.Write(path);
        var read = WwiseEventSidecar.Read(path);

        Assert.Equal(sidecar.Name, read.Name);
        Assert.Equal(sidecar.Category, read.Category);
        Assert.Equal(sidecar.AttenuationMeters, read.AttenuationMeters);
        Assert.Equal(sidecar.Media, read.Media);
        Assert.Contains("\"Footstep\"", File.ReadAllText(path));
    }
}
