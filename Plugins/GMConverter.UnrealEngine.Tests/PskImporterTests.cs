using System.Numerics;
using System.Text.Json;
using GMConverter.Core.Plugins;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
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
        var model = _importer.Parse(psk, new ModelParseOptions(userScale) { Options = new OptionValues(new Dictionary<string, object?> { [PSKImporterOptions.AnimationPath] = psa }) });
        var bounds = model.Bounds();
        Assert.Equal(userScale, bounds.Max.X - bounds.Min.X, 5);
        Assert.Equal(0.1f * userScale, bounds.Min.X, 5);
        Assert.Equal(0.25f * userScale, model.Skeleton!.Bones[0].LocalBindPose.Translation.X, 5);
        var track = Assert.IsType<BoneTransformTrack>(Assert.Single(Assert.Single(model.Animations!).Tracks));
        Assert.Equal(0.5f * userScale, Assert.Single(track.Keyframes).Transform.Translation.X, 5);
        Assert.Equal(new Vector3(2, 3, 4), track.Keyframes[0].Transform.Scale);
    }

    [Fact]
    public void SeveralAnimationFilesAreBundledWithUniqueClipNames()
    {
        var (psk, psa) = PskFixtures.WriteFixture();
        var (_, otherPsa) = PskFixtures.WriteFixture();
        var paths = $"{psa}{OptionDescriptor.MultipleValueSeparator} {otherPsa}";

        var model = _importer.Parse(psk, new ModelParseOptions(1f) { Options = new OptionValues(new Dictionary<string, object?> { [PSKImporterOptions.AnimationPath] = paths }) });

        Assert.Equal(["move", "move_2"], model.Animations!.Select(clip => clip.Name));
    }

    [Fact]
    public void AnimationOptionMustNameAnExistingPsaFile()
    {
        var (psk, psa) = PskFixtures.WriteFixture();
        var notPsa = Path.ChangeExtension(psa, ".txt");
        File.Copy(psa, notPsa);

        ModelParseOptions WithAnimation(string path) => new(1f)
        {
            Options = new OptionValues(new Dictionary<string, object?> { [PSKImporterOptions.AnimationPath] = path }),
        };

        Assert.Contains(".psa", Assert.Throws<GMConverterException>(() => _importer.Parse(psk, WithAnimation(notPsa))).Message);
        Assert.Throws<GMConverterException>(() => _importer.Parse(psk, WithAnimation(psa + ".missing")));
        Assert.Contains("animation-path", Assert.Single(_importer.OptionSchema.AllOptions).Aliases);
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
    public void SceneKeepsSkeletonAndAppliesAnimation()
    {
        var (psk, psa) = PskFixtures.WriteFixture();
        var scene = WriteScene(Path.GetDirectoryName(psk)!, Path.GetFileName(psk));

        var model = _importer.Parse(scene, new ModelParseOptions(1f) { Options = new OptionValues(new Dictionary<string, object?> { [PSKImporterOptions.AnimationPath] = psa }) });

        Assert.Equal("root", Assert.Single(model.Skeleton!.Bones).Name);
        Assert.All(Assert.Single(model.Meshes).Vertices, vertex => Assert.Equal(0, Assert.Single(vertex.BoneWeights!).BoneIndex));
        var track = Assert.IsType<BoneTransformTrack>(Assert.Single(Assert.Single(model.Animations!).Tracks));
        Assert.Equal(0.5f, Assert.Single(track.Keyframes).Transform.Translation.X, 5);
    }

    [Fact]
    public void SceneMergesPartSkeletonsByBoneName()
    {
        // A modular character: body and head share "root" but each adds its own bone, and a
        // boneless prop rides along. Weights must follow their bone into the merged skeleton.
        var directory = PskFixtures.CreateDirectory();
        PskFixtures.WriteSkinnedTriangle(Path.Join(directory, "body.psk"), [("root", -1), ("spine", 0)], weightedBone: 1);
        PskFixtures.WriteSkinnedTriangle(Path.Join(directory, "head.psk"), [("root", -1), ("head", 0)], weightedBone: 1, offsetX: 200);
        PskFixtures.WriteSkinnedTriangle(Path.Join(directory, "prop.pskx"), [], weightedBone: 0, offsetX: 400);
        var scene = WriteScene(directory, "body.psk", "head.psk", "prop.pskx");

        var model = _importer.Parse(scene, new ModelParseOptions(1f));

        Assert.Equal(["root", "spine", "head"], model.Skeleton!.Bones.Select(bone => bone.Name));
        Assert.Equal([-1, 0, 0], model.Skeleton.Bones.Select(bone => bone.ParentIndex));
        Assert.Equal([1, 2], model.Meshes.Take(2).Select(mesh => mesh.Vertices.Select(vertex => Assert.Single(vertex.BoneWeights!).BoneIndex).Distinct().Single()));
        Assert.All(model.Meshes[2].Vertices, vertex => Assert.Null(vertex.BoneWeights));
    }

    [Fact]
    public void SceneIgnoresUnweightedSocketBones()
    {
        var directory = PskFixtures.CreateDirectory();
        PskFixtures.WriteSkinnedTriangle(Path.Join(directory, "hull.pskx"), [("fx_engine_l", -1), ("fx_engine_r", -1)], weightedBone: -1);
        var scene = WriteScene(directory, "hull.pskx");

        Assert.Null(_importer.Parse(scene, new ModelParseOptions(1f)).Skeleton);
    }

    private static string WriteScene(string directory, params string[] parts)
    {
        var scene = Path.Join(directory, "unit.ue4scene");
        File.WriteAllText(scene, JsonSerializer.Serialize(new
        {
            Version = 1,
            Name = "unit scene",
            Entries = parts.Select(part => new
            {
                Path = part,
                Transform = new
                {
                    Translation = new { X = 0, Y = 0, Z = 0 },
                    Rotation = new { X = 0, Y = 0, Z = 0, W = 1 },
                    Scale = new { X = 1, Y = 1, Z = 1 }
                }
            })
        }));
        return scene;
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
    public void MaskedUnrealMaterialWithClearTexelsIsAlphaTested()
    {
        var material = ParseUnrealMaterial("Masked", halfCutOut: true);

        Assert.Equal(MaterialBlendMode.AlphaTest, material.BlendMode);
        Assert.Equal(0.4f, material.AlphaCutoff, 5);
        Assert.True(material.HasAlpha);
    }

    [Fact]
    public void MaskedUnrealMaterialWithoutClearTexelsStaysOpaque()
    {
        // Alpha here is packed data (e.g. a dithered-fade mask), not a cut-out.
        var material = ParseUnrealMaterial("Masked", halfCutOut: false);

        Assert.Equal(MaterialBlendMode.Unspecified, material.BlendMode);
        Assert.False(material.HasAlpha);
    }

    [Fact]
    public void TranslucentUnrealMaterialBlendsDiffuseAlpha()
    {
        var material = ParseUnrealMaterial("Translucent", halfCutOut: false);

        Assert.Equal(MaterialBlendMode.AlphaBlend, material.BlendMode);
        Assert.True(material.HasAlpha);
    }

    // halfCutOut writes a diffuse whose top half is fully clear (a real cut-out such as netting);
    // otherwise every texel carries the same partial alpha, like packed data.
    private Material ParseUnrealMaterial(string blendMode, bool halfCutOut)
    {
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(
            Path.Join(directory, "test.json"),
            $$"""{ "Textures": { "Diffuse": "panel" }, "BlendMode": "{{blendMode}}", "AlphaCutoff": 0.4 }""");
        const int size = 4;
        var pixels = new byte[size * size * 4];
        for (var texel = 0; texel < size * size; texel++)
        {
            pixels[(texel * 4) + 0] = 80;
            pixels[(texel * 4) + 3] = halfCutOut ? (byte)(texel < size * size / 2 ? 0 : 255) : (byte)128;
        }

        using var diffuse = new DefaultTextureFactory().FromRgba("panel", size, size, pixels, hasAlpha: true);
        diffuse.WritePng(Path.Join(directory, "panel.png"));

        return Assert.Single(_importer.Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory))).Materials);
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
