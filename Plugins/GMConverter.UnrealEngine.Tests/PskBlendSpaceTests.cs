using GMConverter.Core.Textures;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;
using GMConverter.UnrealEngine.Formats.PSA;
using GMConverter.UnrealEngine.Importers;

namespace GMConverter.UnrealEngine.Tests;

public sealed class PskBlendSpaceTests
{
    private readonly PSKImporter _importer = new(new DefaultTextureFactory());

    [Fact]
    public void AimOffsetSidecarBecomesAnAimBlendSpace()
    {
        var (psk, psa) = PskFixtures.WriteFixture();
        var second = Path.ChangeExtension(psa, ".second.psa");
        File.Copy(psa, second);
        var sidecar = WriteSidecar(psk, new PSABlendSpaceSidecar(
            "AO_Test",
            IsAimOffset: true,
            [new PSABlendSpaceAxis("Yaw", -90, 90), new PSABlendSpaceAxis("Pitch", -45, 45)],
            [new PSABlendSpaceSample("move", [-90, -45]), new PSABlendSpaceSample("move_2", [90, 45])]));

        var model = Import(psk, $"{psa};{second}", sidecar);

        var blendSpace = Assert.Single(model.BlendSpaces!);
        Assert.Equal("AO_Test", blendSpace.Name);
        Assert.Equal([BlendAxis.AimYaw, BlendAxis.AimPitch], blendSpace.Parameters.Select(parameter => parameter.Axis));
        Assert.Equal(new BlendParameter("Pitch", BlendAxis.AimPitch, -45, 45), blendSpace.Parameters[1]);
        Assert.Equal(["move", "move_2"], blendSpace.Samples.Select(sample => sample.ClipName));
        Assert.Equal([90f, 45f], blendSpace.Samples[1].Coordinates);
    }

    [Fact]
    public void BlendSpacesWithMissingClipsAreSkipped()
    {
        var (psk, psa) = PskFixtures.WriteFixture();
        var sidecar = WriteSidecar(psk, new PSABlendSpaceSidecar(
            "BS_Test",
            IsAimOffset: false,
            [new PSABlendSpaceAxis("Direction", -180, 180)],
            [new PSABlendSpaceSample("move", [0]), new PSABlendSpaceSample("not_imported", [90])]));

        Assert.Null(Import(psk, psa, sidecar).BlendSpaces);
    }

    [Theory]
    [InlineData("Direction", false, BlendAxis.MoveDirection)]
    [InlineData("Speed", false, BlendAxis.MoveSpeed)]
    [InlineData("Yaw", false, BlendAxis.Generic)]
    [InlineData("AimYaw", false, BlendAxis.AimYaw)]
    [InlineData("Left/Right", true, BlendAxis.Generic)]
    public void AxesAreGuessedFromTheirNames(string name, bool isAimOffset, BlendAxis axis)
    {
        var (psk, psa) = PskFixtures.WriteFixture();
        var sidecar = WriteSidecar(psk, new PSABlendSpaceSidecar(
            "BS_Test", isAimOffset, [new PSABlendSpaceAxis(name, 0, 600)], [new PSABlendSpaceSample("move", [300])]));

        var parameter = Assert.Single(Assert.Single(Import(psk, psa, sidecar).BlendSpaces!).Parameters);

        Assert.Equal(axis, parameter.Axis);
        // Speeds are centimetres per second, scaled to the model's units like positions.
        Assert.Equal(axis is BlendAxis.MoveSpeed ? 6f : 600f, parameter.Max, 3);
    }

    private SDK.Geometry.Model Import(string psk, string animations, string sidecar)
    {
        return _importer.Import(new ModelImportContext(psk)
        {
            Options = new OptionValues(new Dictionary<string, object?>
            {
                [PSKImporterOptions.AnimationPath] = animations,
                [PSKImporterOptions.BlendSpacePath] = sidecar,
            }),
        });
    }

    private static string WriteSidecar(string psk, PSABlendSpaceSidecar sidecar)
    {
        var path = Path.Join(Path.GetDirectoryName(psk)!, sidecar.Name + PSABlendSpaceSidecar.Extension);
        sidecar.Write(path);
        return path;
    }
}
