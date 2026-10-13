using GMConverter.Core.Textures;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;
using GMConverter.UnrealEngine.Formats.PSA;
using GMConverter.UnrealEngine.Importers;

namespace GMConverter.UnrealEngine.Tests;

public sealed class PskClipEventTests
{
    [Fact]
    public void ClipSidecarEventsBecomeAnimationEvents()
    {
        var (psk, psa) = PskFixtures.WriteFixture();
        new PSAClipSidecar(false, true,
        [
            new PSAClipEvent(0.25f, AnimationEventKind.Footstep, Bone: "ball_l"),
            new PSAClipEvent(0.5f, AnimationEventKind.Sound, "FLY_B1_Short_Light"),
        ]).Write(psa);

        var model = new PSKImporter(new DefaultTextureFactory()).Import(new ModelImportContext(psk)
        {
            Options = new OptionValues(new Dictionary<string, object?> { [PSKImporterOptions.AnimationPath] = psa }),
        });

        var clip = Assert.Single(model.Animations!);
        Assert.True(clip.Loops);
        Assert.Equal(
            [new AnimationEvent(0.25f, AnimationEventKind.Footstep, Bone: "ball_l"), new AnimationEvent(0.5f, AnimationEventKind.Sound, "FLY_B1_Short_Light")],
            clip.Events);
    }

    [Fact]
    public void SidecarsWithOnlyEventsAreWorthWriting()
    {
        Assert.False(new PSAClipSidecar(false, false).HasData);
        Assert.True(new PSAClipSidecar(false, false, [new PSAClipEvent(0f, AnimationEventKind.FireWeapon)]).HasData);
    }
}
