using GMConverter.Core.Textures;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Geometry;
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
        Assert.True(new PSAClipSidecar(false, false, MeshTranslationBones: ["root"]).HasData);
    }

    [Fact]
    public void MeshTranslationBonesKeepTheMeshsOwnOffset()
    {
        // The fixture's root sits 25 cm out in the mesh and 50 cm out in the animation.
        var (psk, psa) = PskFixtures.WriteFixture();
        Transform FirstKey()
        {
            var model = new PSKImporter(new DefaultTextureFactory()).Import(new ModelImportContext(psk)
            {
                Options = new OptionValues(new Dictionary<string, object?> { [PSKImporterOptions.AnimationPath] = psa }),
            });
            return Assert.IsType<BoneTransformTrack>(Assert.Single(Assert.Single(model.Animations!).Tracks)).Keyframes[0].Transform;
        }

        var animated = FirstKey().Translation;
        new PSAClipSidecar(false, false, MeshTranslationBones: ["root"]).Write(psa);
        var rotationOnly = FirstKey().Translation;

        Assert.Equal(0.5f, animated.Length(), 4);
        Assert.Equal(0.25f, rotationOnly.Length(), 4);
    }
}
