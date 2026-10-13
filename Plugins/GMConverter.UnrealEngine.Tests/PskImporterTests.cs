using System.Numerics;
using System.Text.Json;
using GMConverter.Core.Textures;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
using GMConverter.SDK.Physics;
using GMConverter.UnrealEngine.Formats.PSA;
using GMConverter.UnrealEngine.Formats.PSK;
using GMConverter.UnrealEngine.Formats.PSK.Physics;
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
        var model = _importer.Import(new ModelImportContext(psk, userScale) { Options = new OptionValues(new Dictionary<string, object?> { [PSKImporterOptions.AnimationPath] = psa }) });
        var bounds = model.Bounds();
        Assert.Equal(userScale, bounds.Max.X - bounds.Min.X, 5);
        Assert.Equal(0.1f * userScale, bounds.Min.X, 5);
        Assert.Equal(0.25f * userScale, model.Skeleton!.Bones[0].LocalBindPose.Translation.X, 5);
        var track = Assert.IsType<BoneTransformTrack>(Assert.Single(Assert.Single(model.Animations!).Tracks));
        Assert.Equal(0.5f * userScale, Assert.Single(track.Keyframes).Transform.Translation.X, 5);
        Assert.Equal(new Vector3(2, 3, 4), track.Keyframes[0].Transform.Scale);
    }

    [Fact]
    public void ClipSidecarMarksRootMotionAndLooping()
    {
        var (psk, psa) = PskFixtures.WriteFixture();
        AnimationClip Import() => Assert.Single(_importer.Import(new ModelImportContext(psk) { Options = new OptionValues(new Dictionary<string, object?> { [PSKImporterOptions.AnimationPath] = psa }) }).Animations!);

        Assert.Equal((false, false), (Import().RootMotion, Import().Loops));

        new PSAClipSidecar(RootMotion: true, Loops: true).Write(psa);

        Assert.Equal((true, true), (Import().RootMotion, Import().Loops));
    }

    [Fact]
    public void SeveralAnimationFilesAreBundledWithUniqueClipNames()
    {
        var (psk, psa) = PskFixtures.WriteFixture();
        var (_, otherPsa) = PskFixtures.WriteFixture();
        var paths = $"{psa}{OptionDescriptor.MultipleValueSeparator} {otherPsa}";

        var model = _importer.Import(new ModelImportContext(psk) { Options = new OptionValues(new Dictionary<string, object?> { [PSKImporterOptions.AnimationPath] = paths }) });

        Assert.Equal(["move", "move_2"], model.Animations!.Select(clip => clip.Name));
    }

    [Fact]
    public void LayeredAnimationsShareTheirBasePose()
    {
        var (psk, first) = PskFixtures.WriteFixture();
        var (_, second) = PskFixtures.WriteFixture();
        // Each additive is exported with its own copy of the base, so the game path identifies it.
        var (_, basePsa) = PskFixtures.WriteFixture();
        var (_, baseCopy) = PskFixtures.WriteFixture();
        new PSALayerSidecar(LayerSpace.Model, Path.GetRelativePath(Path.GetDirectoryName(first)!, basePsa), 0, "/Game/A_Idle").Write(first);
        new PSALayerSidecar(LayerSpace.Model, Path.GetRelativePath(Path.GetDirectoryName(second)!, baseCopy), 0, "/Game/A_Idle").Write(second);
        var paths = $"{first}{OptionDescriptor.MultipleValueSeparator}{second}";

        var model = _importer.Import(new ModelImportContext(psk) { Options = new OptionValues(new Dictionary<string, object?> { [PSKImporterOptions.AnimationPath] = paths }) });

        var layers = model.Animations!.Select(clip => clip.Layering!).ToArray();
        Assert.All(layers, layer => Assert.Equal(LayerSpace.Model, layer.Space));
        Assert.Same(layers[0].Base, layers[1].Base);
        Assert.Equal(0.5f, Assert.Single(layers[0].Base!.BoneTransforms).Value.Translation.X, 5);
    }

    [Fact]
    public void LayerWithoutBaseFileFallsBackToPlainClip()
    {
        var (psk, restLayer) = PskFixtures.WriteFixture();
        var (_, orphanLayer) = PskFixtures.WriteFixture();
        new PSALayerSidecar(LayerSpace.Local, null, 0).Write(restLayer);
        new PSALayerSidecar(LayerSpace.Local, "missing.psa", 0).Write(orphanLayer);

        AnimationClip Import(string psa) => Assert.Single(_importer.Import(new ModelImportContext(psk) { Options = new OptionValues(new Dictionary<string, object?> { [PSKImporterOptions.AnimationPath] = psa }) }).Animations!);

        var restLayering = Import(restLayer).Layering!;
        Assert.Equal(LayerSpace.Local, restLayering.Space);
        Assert.Null(restLayering.Base);
        Assert.Null(Import(orphanLayer).Layering);
    }

    [Fact]
    public void AnimationOptionMustNameAnExistingPsaFile()
    {
        var (psk, psa) = PskFixtures.WriteFixture();
        var notPsa = Path.ChangeExtension(psa, ".txt");
        File.Copy(psa, notPsa);

        ModelImportContext WithAnimation(string path) => new(psk)
        {
            Options = new OptionValues(new Dictionary<string, object?> { [PSKImporterOptions.AnimationPath] = path }),
        };

        Assert.Contains(".psa", Assert.Throws<GMConverterException>(() => _importer.Import(WithAnimation(notPsa))).Message);
        Assert.Throws<GMConverterException>(() => _importer.Import(WithAnimation(psa + ".missing")));
        Assert.Contains("animation-path", _importer.OptionSchema.AllOptions.Single(option => option.Key == PSKImporterOptions.AnimationPath).Aliases);
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
        var model = _importer.Import(new ModelImportContext(scene, userScale));
        var bounds = model.Bounds();
        Assert.Equal(2.1f * userScale, bounds.Min.X, 5);
        Assert.Equal(userScale, bounds.Max.X - bounds.Min.X, 5);
    }

    [Fact]
    public void SceneKeepsSkeletonAndAppliesAnimation()
    {
        var (psk, psa) = PskFixtures.WriteFixture();
        var scene = WriteScene(Path.GetDirectoryName(psk)!, Path.GetFileName(psk));

        var model = _importer.Import(new ModelImportContext(scene) { Options = new OptionValues(new Dictionary<string, object?> { [PSKImporterOptions.AnimationPath] = psa }) });

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

        var model = _importer.Import(new ModelImportContext(scene));

        Assert.Equal(["root", "spine", "head"], model.Skeleton!.Bones.Select(bone => bone.Name));
        Assert.Equal([-1, 0, 0], model.Skeleton.Bones.Select(bone => bone.ParentIndex));
        Assert.Equal([1, 2], model.Meshes.Take(2).Select(mesh => mesh.Vertices.Select(vertex => Assert.Single(vertex.BoneWeights!).BoneIndex).Distinct().Single()));
        Assert.All(model.Meshes[2].Vertices, vertex => Assert.Null(vertex.BoneWeights));
    }

    [Fact]
    public void SceneLodsComeFromTheSidecarAndFollowTheMergedSkeleton()
    {
        // The body has a second level whose PSK lists its bones in another order; the head has none,
        // so the scene's level keeps the head's full mesh.
        var directory = PskFixtures.CreateDirectory();
        var body = Path.Join(directory, "body.psk");
        PskFixtures.WriteSkinnedTriangle(body, [("root", -1), ("spine", 0)], weightedBone: 1);
        PskFixtures.WriteSkinnedTriangle(Path.Join(directory, "body_LOD1.psk"), [("root", -1), ("pelvis", 0), ("spine", 0)], weightedBone: 2);
        new PSKLodSidecar([new PSKLodEntry("body_LOD1.psk", 0.4f), new PSKLodEntry("missing_LOD2.psk", 0.2f)]).Write(body);
        PskFixtures.WriteSkinnedTriangle(Path.Join(directory, "head.psk"), [("root", -1), ("head", 0)], weightedBone: 1, offsetX: 200);

        var model = _importer.Import(new ModelImportContext(WriteScene(directory, "body.psk", "head.psk")));

        var lod = Assert.Single(model.Lods!);
        Assert.Equal(0.4f, lod.ScreenSize);
        Assert.Equal(2, lod.Meshes.Count);
        // Body weights land on "spine" and head weights on "head" in the merged skeleton.
        Assert.Equal(["root", "spine", "head"], model.Skeleton!.Bones.Select(bone => bone.Name));
        Assert.Equal([1, 2], lod.Meshes.Select(mesh => mesh.Vertices.Select(vertex => Assert.Single(vertex.BoneWeights!).BoneIndex).Distinct().Single()));
        Assert.Equal(model.Meshes[0].Submeshes.Select(submesh => submesh.MaterialName), lod.Meshes[0].Submeshes.Select(submesh => submesh.MaterialName));
    }

    [Fact]
    public void ScenePhysicsBodiesAndJointsFollowTheMergedSkeleton()
    {
        // Merged bones: root 0, spine 1, head 2. The body on a bone the mesh doesn't have is
        // dropped, and so is the joint that needs it.
        var directory = PskFixtures.CreateDirectory();
        PskFixtures.WriteSkinnedTriangle(Path.Join(directory, "body.psk"), [("root", -1), ("spine", 0)], weightedBone: 1);
        PskFixtures.WriteSkinnedTriangle(Path.Join(directory, "head.psk"), [("root", -1), ("head", 0)], weightedBone: 1);
        new PSKPhysicsSidecar(
            [new PSKPhysicsBody("spine", [new PSKPhysicsShape(PSKPhysicsShapeKind.Capsule, new Vector3(0, 0, 20), Quaternion.Identity, Radius: 10, Length: 50)])],
            []).Write(Path.Join(directory, "body.psk"));
        new PSKPhysicsSidecar(
            [
                new PSKPhysicsBody("head", [new PSKPhysicsShape(PSKPhysicsShapeKind.Sphere, Vector3.Zero, Quaternion.Identity, Radius: 15)]),
                new PSKPhysicsBody("root", [new PSKPhysicsShape(PSKPhysicsShapeKind.Box, Vector3.Zero, Quaternion.Identity, HalfExtents: new Vector3(5))]),
                new PSKPhysicsBody("tail", [new PSKPhysicsShape(PSKPhysicsShapeKind.Sphere, Vector3.Zero, Quaternion.Identity, Radius: 5)]),
            ],
            [
                new PSKPhysicsJoint("head", "root", Quaternion.Identity, TwistLimit: 0, Swing1Limit: 30, Swing2Limit: null),
                new PSKPhysicsJoint("tail", "root", Quaternion.Identity, 10, 10, 10),
            ]).Write(Path.Join(directory, "head.psk"));

        var model = _importer.Import(new ModelImportContext(WriteScene(directory, "body.psk", "head.psk")));

        // UE4/5 characters face +Y, which ActorX's Y mirror makes -Y.
        Assert.Equal(-Vector3.UnitY, model.Forward);
        var physics = model.Physics!;
        Assert.Equal([1, 2, 0], physics.Bodies.Select(body => body.BoneIndex));
        var capsule = Assert.IsType<CapsuleShape>(Assert.Single(physics.Bodies[0].Shapes));
        Assert.Equal(0.1f, capsule.Radius, 5);
        Assert.Equal(0.5f, capsule.Length, 5);
        Assert.Equal(0.2f, capsule.LocalTransform.Translation.Z, 5);
        var joint = Assert.Single(physics.Joints);
        Assert.Equal((2, 0), (joint.BoneIndex, joint.ParentBoneIndex));
        // Twist turns about X, swing 2 about Y and swing 1 about Z.
        Assert.Equal(new AngularLimit(0, 0), joint.X);
        Assert.Null(joint.Y);
        Assert.Equal(new AngularLimit(-30, 30), joint.Z);
    }

    [Fact]
    public void SceneSocketsBecomeAttachmentsOnTheMergedSkeleton()
    {
        // The head's own bone list puts "head" at 1; in the merged skeleton it is 2. The prop has
        // no bones, so its socket is placed relative to the model.
        var directory = PskFixtures.CreateDirectory();
        PskFixtures.WriteSkinnedTriangle(Path.Join(directory, "body.psk"), [("root", -1), ("spine", 0)], weightedBone: 1);
        PskFixtures.WriteSkinnedTriangle(Path.Join(directory, "head.psk"), [("root", -1), ("head", 0)], weightedBone: 1);
        PskFixtures.WriteSkinnedTriangle(Path.Join(directory, "prop.pskx"), [], weightedBone: 0);
        new PSKSocketSidecar([
            new PSKSocket("hat", "head", new Vector3(100, 0, 0), Quaternion.Identity),
            new PSKSocket("missing_bone", "tail", Vector3.Zero, Quaternion.Identity),
        ]).Write(Path.Join(directory, "head.psk"));
        new PSKSocketSidecar([new PSKSocket("muzzle", null, new Vector3(50, 0, 0), Quaternion.Identity)])
            .Write(Path.Join(directory, "prop.pskx"));

        var model = _importer.Import(new ModelImportContext(WriteScene(directory, "body.psk", "head.psk", "prop.pskx")));

        var attachments = model.Attachments!;
        Assert.Equal(["hat", "muzzle"], attachments.Select(attachment => attachment.Name));
        Assert.Equal(2, attachments[0].BoneIndex);
        Assert.Equal(1f, attachments[0].LocalTransform.Translation.X, 5);
        Assert.Null(attachments[1].BoneIndex);
        Assert.Equal(0.5f, attachments[1].LocalTransform.Translation.X, 5);
    }

    [Fact]
    public void SceneIgnoresUnweightedSocketBones()
    {
        var directory = PskFixtures.CreateDirectory();
        PskFixtures.WriteSkinnedTriangle(Path.Join(directory, "hull.pskx"), [("fx_engine_l", -1), ("fx_engine_r", -1)], weightedBone: -1);
        var scene = WriteScene(directory, "hull.pskx");

        Assert.Null(_importer.Import(new ModelImportContext(scene)).Skeleton);
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

        var model = _importer.Import(new ModelImportContext(psk, Materials: new MaterialResolveOptions(directory)));

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

        return Assert.Single(_importer.Import(new ModelImportContext(psk, Materials: new MaterialResolveOptions(directory))).Materials);
    }

    [Fact]
    public void NormalSidecarKeyIsNotMistakenForPackedSpecular()
    {
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=panel\nNormal=panel_bump\n");
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 255);
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "panel_bump.tga"), alpha: 255);

        var material = Assert.Single(_importer.Import(new ModelImportContext(psk, Materials: new MaterialResolveOptions(directory))).Materials);

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

        var material = Assert.Single(_importer.Import(new ModelImportContext(psk, Materials: new MaterialResolveOptions(directory))).Materials);

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

        var material = Assert.Single(_importer.Import(new ModelImportContext(psk, Materials: new MaterialResolveOptions(directory))).Materials);

        Assert.Null(material.EmissiveTexture);
    }

    [Fact]
    public void InsideOutClosedShellIsRewoundToMatchTheMesh()
    {
        var directory = PskFixtures.CreateDirectory();
        var psk = Path.Join(directory, "shells.psk");
        PskFixtures.WriteTetrahedraFixture(psk, invertedShell: 2);

        var model = _importer.Import(new ModelImportContext(psk));
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

        var material = Assert.Single(_importer.Import(new ModelImportContext(psk, Materials: new MaterialResolveOptions(directory))).Materials);

        // lerp(panel R80 G220 B240, scorch R200 G0 B0, 51/255 = 0.2).
        Assert.Equal([104, 176, 192], material.DiffuseTexture!.GetRgbaPixels()[..3]);
    }

    [Fact]
    public void PlainPskFilesLeaveTheFacingUnknown()
    {
        // A bare PSK may come from UE2, whose characters face +X rather than UE4/5's +Y.
        var (psk, _) = PskFixtures.WriteFixture();

        Assert.Null(_importer.Import(new ModelImportContext(psk)).Forward);
    }
}
