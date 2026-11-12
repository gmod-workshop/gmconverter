using System.Numerics;
using GMConverter.Core.Textures;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Explorer;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Profiles;
using GMConverter.UnrealEngine.Formats.PSK;
using GMConverter.UnrealEngine.Importers;
using GMConverter.UnrealEngine.Profiles.CrossFireHD;

namespace GMConverter.UnrealEngine.Tests;

public sealed class CrossFireHDProfileTests
{
    [Fact]
    public void FirstPersonClipsGetTheirRoles()
    {
        AnimationClip Clip(string name) => new(name, 30f, 1f, []);
        var model = new Model("v_kukri", [], [], Animations: [Clip("puton"), Clip("Idle"), Clip("fire1"), Clip("fire2"), Clip("Put_Down"), Clip("Inspect"), Clip("Crouch_begin")]);

        var prepared = new CrossFireHDProfile().Prepare(model);

        Assert.Equal(
            [AnimationAction.Draw, AnimationAction.Idle, AnimationAction.Attack, AnimationAction.SecondaryAttack, AnimationAction.Holster, AnimationAction.Inspect, null],
            prepared.Animations!.Select(clip => clip.Role?.Action));
    }

    [Fact]
    public void InstallsAreRecognisedFromTheGameExecutable()
    {
        var install = PskFixtures.CreateDirectory();
        var content = Directory.CreateDirectory(Path.Join(install, "Cross", "Content")).FullName;
        var profile = new CrossFireHDProfile();
        Assert.Equal(GameMatch.None, profile.Detect(new ExplorerTarget(content)));

        Directory.CreateDirectory(Path.Join(install, "Binaries", "Win64"));
        File.WriteAllText(Path.Join(install, "Binaries", "Win64", "CFHD-The10thZone.exe"), string.Empty);

        Assert.Equal(GameMatch.Exact, profile.Detect(new ExplorerTarget(install)));
        Assert.Equal(GameMatch.Exact, profile.Detect(new ExplorerTarget(content)));
    }

    [Fact]
    public void ModelSidecarSetsFacingAndFirstPerson()
    {
        var (psk, _) = PskFixtures.WriteFixture();
        var importer = new PSKImporter(new DefaultTextureFactory());
        var plain = importer.Import(new ModelImportContext(psk));
        Assert.Null(plain.Forward);
        Assert.False(plain.FirstPerson);

        // Facing -Y in the PSK's mirrored space is +Y in Unreal's, like CrossFire HD's arms.
        new PSKModelSidecar(-Vector3.UnitY, FirstPerson: true).Write(psk);
        var viewmodel = importer.Import(new ModelImportContext(psk));

        Assert.True(viewmodel.FirstPerson);
        Assert.NotNull(viewmodel.Forward);
        Assert.Equal(1f, viewmodel.Forward!.Value.Length(), 4);
    }
}
