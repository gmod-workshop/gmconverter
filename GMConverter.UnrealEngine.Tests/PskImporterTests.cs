using System.Numerics;
using System.Text.Json;
using GMConverter.Plugins;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.UnrealEngine.Importers;

namespace GMConverter.UnrealEngine.Tests;

public sealed class PskImporterTests
{
    private readonly PSKImporter _importer = new(new DefaultTextureFactory());

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(0.5f)]
    public void StandaloneMeshSkeletonAndAnimationUseMeters(float userScale)
    {
        var (psk, psa) = PskFixtures.WriteFixture();
        var model = _importer.Parse(psk, new ModelParseOptions(userScale, AnimationPath: psa));
        var bounds = model.Bounds();
        Assert.Equal(userScale, bounds.Max.X - bounds.Min.X, 5);
        Assert.Equal(0.1f * userScale, bounds.Min.X, 5);
        Assert.Equal(0.25f * userScale, model.Skeleton!.Bones[0].LocalBindPose.Translation.X, 5);
        var track = Assert.IsType<BoneTransformTrack>(Assert.Single(Assert.Single(model.Animations!).Tracks));
        Assert.Equal(0.5f * userScale, Assert.Single(track.Keyframes).Transform.Translation.X, 5);
        Assert.Equal(new Vector3(2, 3, 4), track.Keyframes[0].Transform.Scale);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void SceneMeshAndInstanceOffsetAreConvertedExactlyOnce(float userScale)
    {
        var (psk, _) = PskFixtures.WriteFixture();
        var scene = Path.ChangeExtension(psk, ".ue4scene");
        File.WriteAllText(scene, JsonSerializer.Serialize(new
        {
            Version = 1,
            Name = "unit scene",
            Entries = new[]
            {
                new
                {
                    Path = Path.GetFileName(psk),
                    Transform = new
                    {
                        Translation = new { X = 200, Y = 0, Z = 0 },
                        Rotation = new { X = 0, Y = 0, Z = 0, W = 1 },
                        Scale = new { X = 1, Y = 1, Z = 1 }
                    }
                }
            }
        }));
        var model = _importer.Parse(scene, new ModelParseOptions(userScale));
        var bounds = model.Bounds();
        Assert.Equal(2.1f * userScale, bounds.Min.X, 5);
        Assert.Equal(userScale, bounds.Max.X - bounds.Min.X, 5);
    }

    [Fact]
    public void OpacityTextureKeepsAlphaWhenTgaHeaderDeclaresNoAlphaBits()
    {
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=fluid\nOpacity=fluid\n");
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "fluid.tga"), alpha: 128);

        var model = _importer.Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory)));

        var material = Assert.Single(model.Materials);
        Assert.True(material.HasAlpha);
        var pixels = material.DiffuseTexture!.GetRgbaPixels();
        Assert.All(Enumerable.Range(0, pixels.Length / 4), index => Assert.Equal(128, pixels[(index * 4) + 3]));
    }

    [Fact]
    public void NormalSidecarKeyIsNotMistakenForPackedSpecular()
    {
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=panel\nNormal=panel_bump\n");
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 255);
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "panel_bump.tga"), alpha: 255);

        var material = Assert.Single(_importer.Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory))).Materials);

        Assert.Equal("panel_bump", material.NormalTexture?.Name);
        Assert.Null(material.SpecularTexture);
    }

    [Fact]
    public void MaskedSelfIlluminationResamplesSmallerGlowColourToMask()
    {
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=panel\nSelfIllumination=flash\nSelfIlluminationMask=panel\n");
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 51, size: 4);
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "flash.tga"), alpha: 255, size: 2, blue: 0, green: 200, red: 100);

        var material = Assert.Single(_importer.Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory))).Materials);

        // Glow = the 2x2 flash colour (R100 G200 B0) sampled up to the 4x4 mask, x alpha 51/255.
        var glow = material.EmissiveTexture!;
        Assert.Equal((4, 4), (glow.Width, glow.Height));
        Assert.Equal([20, 40, 0, 255], glow.GetRgbaPixels()[^4..]);
    }

    [Fact]
    public void SelfIlluminationMaskWithoutColourLeavesMaterialUnlit()
    {
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=panel\nSelfIlluminationMask=panel\n");
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 51);

        var material = Assert.Single(_importer.Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory))).Materials);

        Assert.Null(material.EmissiveTexture);
    }

    [Fact]
    public void InsideOutClosedShellIsRewoundToMatchTheMesh()
    {
        var directory = PskFixtures.CreateDirectory();
        var psk = Path.Join(directory, "shells.psk");
        PskFixtures.WriteTetrahedraFixture(psk, invertedShell: 2);

        var model = _importer.Parse(psk, new ModelParseOptions(1f));
        var mesh = Assert.Single(model.Meshes);
        var triangles = mesh.Triangles.ToArray();
        Assert.Equal(16, triangles.Length);

        // Shells sit 1 m apart and are 10 cm across, so cluster faces by centroid distance; every
        // face must point away from its own shell's centre once the inside-out one is rewound.
        Vector3 Centroid(Triangle t) => (mesh.Vertices[t.A].Position + mesh.Vertices[t.B].Position + mesh.Vertices[t.C].Position) / 3f;
        var shells = triangles.GroupBy(t => triangles.First(o => Vector3.Distance(Centroid(o), Centroid(t)) < 0.5f)).ToArray();
        Assert.Equal(4, shells.Length);
        foreach (var shell in shells)
        {
            var centre = shell.Aggregate(Vector3.Zero, (sum, t) => sum + Centroid(t)) / shell.Count();
            Assert.All(shell, t => Assert.True(Vector3.Dot(mesh.Vertices[t.A].Normal, Centroid(t) - centre) > 0));
        }
    }

    [Fact]
    public void MaskedDiffuseOverlayIsBlendedIntoDiffuse()
    {
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=panel\nDiffuseOverlay=scorch\nDiffuseOverlayMask=scorch\n");
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 255);
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "scorch.tga"), alpha: 51, blue: 0, green: 0, red: 200);

        var material = Assert.Single(_importer.Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory))).Materials);

        // lerp(panel R80 G220 B240, scorch R200 G0 B0, 51/255 = 0.2).
        Assert.Equal([104, 176, 192], material.DiffuseTexture!.GetRgbaPixels()[..3]);
    }
}
