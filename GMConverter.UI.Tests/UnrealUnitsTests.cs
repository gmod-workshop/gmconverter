using System.Numerics;
using System.Text;
using System.Text.Json;
using GMConverter.Plugins;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;

namespace GMConverter.UI.Tests;

public sealed class UnrealUnitsTests
{
    private static readonly string[] _sourceBlendParameters = ["$additive", "$alphatest\"", "$translucent"];

    [Fact]
    public void SourceCollisionThicknessPreservesSmallModelsAndPadsFlatAxes()
    {
        var bounds = new Bounds(new Vector3(0, 0, 2), new Vector3(0.4f, 0.8f, 2));
        var collision = bounds.WithMinimumThickness(0.0254f);
        Assert.Equal(bounds.Min.X, collision.Min.X);
        Assert.Equal(bounds.Max.X, collision.Max.X);
        Assert.Equal(bounds.Min.Y, collision.Min.Y);
        Assert.Equal(bounds.Max.Y, collision.Max.Y);
        Assert.Equal(0.0254f, collision.Max.Z - collision.Min.Z, 5);
        Assert.Equal(2f, (collision.Min.Z + collision.Max.Z) / 2f, 5);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(0.5f)]
    public void StandaloneMeshSkeletonAndAnimationUseMeters(float userScale)
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, psa) = WriteFixture();
        var importer = PluginHost.Registry.GetImporter("psk")!;
        var model = importer.Parse(psk, new ModelParseOptions(userScale, AnimationPath: psa));
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
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = WriteFixture();
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
        var model = PluginHost.Registry.GetImporter("psk")!.Parse(scene, new ModelParseOptions(userScale));
        var bounds = model.Bounds();
        Assert.Equal(2.1f * userScale, bounds.Min.X, 5);
        Assert.Equal(userScale, bounds.Max.X - bounds.Min.X, 5);
    }

    [Fact]
    public void OpacityTextureKeepsAlphaWhenTgaHeaderDeclaresNoAlphaBits()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=fluid\nOpacity=fluid\n");
        WriteUnlabeledAlphaTga(Path.Join(directory, "fluid.tga"), alpha: 128);

        var model = PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory)));

        var material = Assert.Single(model.Materials);
        Assert.True(material.HasAlpha);
        var pixels = material.DiffuseTexture!.GetRgbaPixels();
        Assert.All(Enumerable.Range(0, pixels.Length / 4), index => Assert.Equal(128, pixels[(index * 4) + 3]));
    }

    [Fact]
    public void SidecarUvScrollBecomesSourceTextureScrollProxy()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=fluid\nUvScroll=0,-0.2\n");
        WriteUnlabeledAlphaTga(Path.Join(directory, "fluid.tga"), alpha: 255);

        var model = PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory)));
        var material = Assert.Single(model.Materials);
        Assert.Equal(new Vector2(0, -0.2f), material.UvScrollRate);
        Assert.NotNull(material.DiffuseTexture);

        // buildMaterials=false makes the exporter write PNG + VMT itself before studiomdl runs;
        // the stub compiler then fails, which is irrelevant to the material output under test.
        var output = Path.Join(directory, "mdl");
        var stubStudioMdl = Path.Join(directory, "cestudiomdl.exe");
        File.WriteAllText(stubStudioMdl, string.Empty);
        _ = Record.Exception(() => PluginHost.Registry.GetExporter("mdl")!.Export(model, output, "triangle", new ExportOptions(
            new Dictionary<string, object?>
            {
                ["studioMdlPath"] = stubStudioMdl,
                ["buildMaterials"] = false
            })));

        var vmt = File.ReadAllText(Directory.GetFiles(output, "test.vmt", SearchOption.AllDirectories).Single());
        Assert.Contains("\"TextureScroll\"", vmt);
        Assert.Contains("\"texturescrollvar\" \"$basetexturetransform\"", vmt);
        Assert.Contains("\"texturescrollrate\" \"0.2\"", vmt);
        Assert.Contains("\"texturescrollangle\" \"-90\"", vmt);
    }

    [Fact]
    public void MaskedSelfIlluminationScrollsAsSourceDetailLayer()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"),
            "Diffuse=fluid\nSelfIllumination=fluid\nSelfIlluminationMask=bubbles\nUvScroll=0.1,0\nEmissiveUvScroll=0.2,0\n");
        WriteUnlabeledAlphaTga(Path.Join(directory, "fluid.tga"), alpha: 255);
        WriteUnlabeledAlphaTga(Path.Join(directory, "bubbles.tga"), alpha: 128);

        var model = PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory)));
        var material = Assert.Single(model.Materials);
        Assert.Equal(new Vector2(0.2f, 0), material.EmissiveUvScrollRate);

        // Glow = the colour texture's average tint (R80 G220 B240) scaled by mask alpha 128/255.
        var glow = material.EmissiveTexture!.GetRgbaPixels();
        Assert.Equal([40, 110, 120, 255], glow[..4]);

        var output = Path.Join(directory, "mdl");
        var stubStudioMdl = Path.Join(directory, "cestudiomdl.exe");
        File.WriteAllText(stubStudioMdl, string.Empty);
        _ = Record.Exception(() => PluginHost.Registry.GetExporter("mdl")!.Export(model, output, "triangle", new ExportOptions(
            new Dictionary<string, object?>
            {
                ["studioMdlPath"] = stubStudioMdl,
                ["buildMaterials"] = false
            })));

        var vmt = File.ReadAllText(Directory.GetFiles(output, "test.vmt", SearchOption.AllDirectories).Single());
        Assert.DoesNotContain("$selfillum", vmt);
        Assert.Contains("\"$detail\" \"gmconverter/test_illum\"", vmt);
        Assert.Contains("\"$detailblendmode\" \"5\"", vmt);
        Assert.Contains("\"texturescrollvar\" \"$basetexturetransform\"", vmt);
        Assert.Contains("\"texturescrollvar\" \"$detailtexturetransform\"", vmt);
        Assert.Contains("\"texturescrollrate\" \"0.2\"", vmt);
    }

    [Fact]
    public void StaticMaskedSelfIlluminationBecomesSourceBaseAlphaSelfIllum()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"),
            "Diffuse=panel\nSelfIllumination=panel\nSelfIlluminationMask=panel\n");
        WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 51);

        var model = PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory)));
        var material = Assert.Single(model.Materials);
        Assert.Null(material.EmissiveUvScrollRate);

        // Glow = colour (R80 G220 B240) x the mask's own alpha (51/255 = 0.2), per pixel.
        Assert.Equal([16, 44, 48, 255], material.EmissiveTexture!.GetRgbaPixels()[..4]);

        var output = Path.Join(directory, "mdl");
        var stubStudioMdl = Path.Join(directory, "cestudiomdl.exe");
        File.WriteAllText(stubStudioMdl, string.Empty);
        _ = Record.Exception(() => PluginHost.Registry.GetExporter("mdl")!.Export(model, output, "triangle", new ExportOptions(
            new Dictionary<string, object?>
            {
                ["studioMdlPath"] = stubStudioMdl,
                ["buildMaterials"] = false
            })));

        // Garry's Mod reads self-illumination from alpha, so opaque glow lives in the base alpha
        // (coverage = glow / albedo = 48 / 240 -> the original mask alpha 51) with no extra mask.
        var vmt = File.ReadAllText(Directory.GetFiles(output, "test.vmt", SearchOption.AllDirectories).Single());
        Assert.Contains("\"$selfillum\" \"1\"", vmt);
        Assert.DoesNotContain("$selfillummask", vmt);
        Assert.DoesNotContain("$translucent", vmt);
        Assert.DoesNotContain("$detail", vmt);
        Assert.Empty(Directory.GetFiles(output, "test_illum.png", SearchOption.AllDirectories));
        using var basePng = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(
            Directory.GetFiles(output, "test.png", SearchOption.AllDirectories).Single());
        Assert.Equal(51, basePng[0, 0].A);
        Assert.Equal(80, basePng[0, 0].R);
    }

    [Fact]
    public void NormalSidecarKeyIsNotMistakenForPackedSpecular()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=panel\nNormal=panel_bump\n");
        WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 255);
        WriteUnlabeledAlphaTga(Path.Join(directory, "panel_bump.tga"), alpha: 255);

        var material = Assert.Single(PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory))).Materials);

        Assert.Equal("panel_bump", material.NormalTexture?.Name);
        Assert.Null(material.SpecularTexture);
    }

    [Fact]
    public void MaskedSelfIlluminationResamplesSmallerGlowColourToMask()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=panel\nSelfIllumination=flash\nSelfIlluminationMask=panel\n");
        WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 51, size: 4);
        WriteUnlabeledAlphaTga(Path.Join(directory, "flash.tga"), alpha: 255, size: 2, blue: 0, green: 200, red: 100);

        var material = Assert.Single(PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory))).Materials);

        // Glow = the 2x2 flash colour (R100 G200 B0) sampled up to the 4x4 mask, x alpha 51/255.
        var glow = material.EmissiveTexture!;
        Assert.Equal((4, 4), (glow.Width, glow.Height));
        Assert.Equal([20, 40, 0, 255], glow.GetRgbaPixels()[^4..]);
    }

    [Fact]
    public void SelfIlluminationMaskWithoutColourLeavesMaterialUnlit()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=panel\nSelfIlluminationMask=panel\n");
        WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 51);

        var material = Assert.Single(PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory))).Materials);

        Assert.Null(material.EmissiveTexture);
    }

    [Theory]
    [InlineData("Blend=Masked\nAlphaRef=127\n", "\"$alphatest\" \"1\"", "\"$alphatestreference\" \"0.498\"")]
    [InlineData("Blend=Additive\n", "\"$additive\" \"1\"", null)]
    [InlineData("Blend=Translucent\n", "\"$translucent\" \"1\"", null)]
    public void SidecarBlendModeReachesSourceVmt(string blendLines, string expected, string? alsoExpected)
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=panel\nOpacity=panel\n" + blendLines);
        WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 200);

        var model = PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory)));
        var output = Path.Join(directory, "mdl");
        var stubStudioMdl = Path.Join(directory, "cestudiomdl.exe");
        File.WriteAllText(stubStudioMdl, string.Empty);
        _ = Record.Exception(() => PluginHost.Registry.GetExporter("mdl")!.Export(model, output, "triangle", new ExportOptions(
            new Dictionary<string, object?>
            {
                ["studioMdlPath"] = stubStudioMdl,
                ["buildMaterials"] = false
            })));

        var vmt = File.ReadAllText(Directory.GetFiles(output, "test.vmt", SearchOption.AllDirectories).Single());
        Assert.Contains(expected, vmt);
        if (alsoExpected is not null)
        {
            Assert.Contains(alsoExpected, vmt);
        }

        // Exactly one Source blend mode is written.
        Assert.Equal(1, _sourceBlendParameters.Count(vmt.Contains));
    }

    [Fact]
    public void InsideOutClosedShellIsRewoundToMatchTheMesh()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var directory = Path.Join(Path.GetTempPath(), "GMConverter.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var psk = Path.Join(directory, "shells.psk");
        WriteTetrahedraFixture(psk, invertedShell: 2);

        var model = PluginHost.Registry.GetImporter("psk")!.Parse(psk, new ModelParseOptions(1f));
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
    public void SmdEulerAnglesKeepRotationAtNinetyDegreePitch()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        // A bone pitched straight up is gimbal locked: roll and yaw spin about the same axis, so
        // the SMD angles must fold the whole spin into one of them rather than emit atan2 noise.
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 6f) *
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var (psk, _) = WriteFixture(rotation);
        var directory = Path.GetDirectoryName(psk)!;
        var model = PluginHost.Registry.GetImporter("psk")!.Parse(psk, new ModelParseOptions(1f));
        var expected = model.Skeleton!.Bones[0].LocalBindPose.Rotation;

        var output = Path.Join(directory, "mdl");
        var stubStudioMdl = Path.Join(directory, "cestudiomdl.exe");
        File.WriteAllText(stubStudioMdl, string.Empty);
        _ = Record.Exception(() => PluginHost.Registry.GetExporter("mdl")!.Export(model, output, "triangle", new ExportOptions(
            new Dictionary<string, object?> { ["studioMdlPath"] = stubStudioMdl })));

        var smd = File.ReadAllLines(Directory.GetFiles(output, "triangle.smd", SearchOption.AllDirectories).Single());
        var bone = smd[Array.IndexOf(smd, "time 0") + 1].Split(' ').Select(value => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        // Source's AngleMatrix composes roll about X, then pitch about Y, then yaw about Z.
        var actual = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, bone[6]) *
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, bone[5]) *
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, bone[4]);
        Assert.Equal(1f, MathF.Abs(Quaternion.Dot(Quaternion.Normalize(expected), actual)), 4);
    }

    // Four closed tetrahedra 100 cm apart; all but `invertedShell` use the winding the bacta
    // dispenser's correct parts use (negative signed volume in raw ActorX space).
    private static void WriteTetrahedraFixture(string path, int invertedShell)
    {
        Vector3[] corners = [new(0, 0, 0), new(10, 0, 0), new(0, 10, 0), new(0, 0, 10)];
        int[][] negativeVolumeFaces = [[0, 1, 2], [0, 3, 1], [0, 2, 3], [1, 3, 2]];
        using var writer = new BinaryWriter(File.Create(path));
        WriteSection(writer, "ACTRHEAD", 0, 0, _ => { });
        WriteSection(writer, "PNTS0000", 12, 16, w =>
        {
            for (var shell = 0; shell < 4; shell++)
            {
                foreach (var corner in corners)
                {
                    WriteVector(w, corner.X + (shell * 100), corner.Y, corner.Z);
                }
            }
        });
        WriteSection(writer, "VTXW0000", 16, 16, w =>
        {
            for (var point = 0; point < 16; point++)
            {
                w.Write(point);
                w.Write(0f);
                w.Write(0f);
                w.Write(0);
            }
        });
        WriteSection(writer, "FACE0000", 12, 16, w =>
        {
            for (var shell = 0; shell < 4; shell++)
            {
                foreach (var face in negativeVolumeFaces)
                {
                    int[] order = shell == invertedShell ? [.. face.Reverse()] : face;
                    foreach (var corner in order)
                    {
                        w.Write((ushort)((shell * 4) + corner));
                    }

                    w.Write((ushort)0);
                    w.Write(1);
                }
            }
        });
        WriteSection(writer, "MATT0000", 88, 1, w =>
        {
            WriteFixedString(w, "test", 64);
            w.Write(new byte[24]);
        });
        WriteSection(writer, "REFSKELT", 120, 1, WriteBone);
        WriteSection(writer, "RAWWEIGHTS", 12, 16, w =>
        {
            for (var point = 0; point < 16; point++)
            {
                w.Write(1f);
                w.Write(point);
                w.Write(0);
            }
        });
    }

    // UModel writes 32-bit TGAs whose image descriptor declares zero alpha bits even when the
    // fourth channel carries real opacity; decoders that trust the header discard it.
    private static void WriteUnlabeledAlphaTga(string path, byte alpha, int size = 2, byte blue = 240, byte green = 220, byte red = 80)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write([0, 0, 2]);
        writer.Write(new byte[9]);
        writer.Write((ushort)size);
        writer.Write((ushort)size);
        writer.Write((byte)32);
        writer.Write((byte)0x20);
        for (var index = 0; index < size * size; index++)
        {
            writer.Write([blue, green, red, alpha]);
        }
    }

    private static (string Psk, string Psa) WriteFixture(Quaternion? rootRotation = null)
    {
        void WriteRootBone(BinaryWriter writer) => WriteBone(writer, rootRotation ?? Quaternion.Identity);

        var directory = Path.Join(Path.GetTempPath(), "GMConverter.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var psk = Path.Join(directory, "triangle.psk");
        var psa = Path.Join(directory, "triangle.psa");
        using (var writer = new BinaryWriter(File.Create(psk)))
        {
            WriteSection(writer, "ACTRHEAD", 0, 0, _ => { });
            WriteSection(writer, "PNTS0000", 12, 3, w =>
            {
                WriteVector(w, 10, 20, 30);
                WriteVector(w, 110, 20, 30);
                WriteVector(w, 10, 120, 30);
            });
            WriteSection(writer, "VTXW0000", 16, 3, w =>
            {
                for (var index = 0; index < 3; index++)
                {
                    w.Write(index);
                    w.Write(index == 1 ? 1f : 0f);
                    w.Write(index == 2 ? 1f : 0f);
                    w.Write(0);
                }
            });
            WriteSection(writer, "FACE0000", 12, 1, w =>
            {
                w.Write((ushort)0);
                w.Write((ushort)1);
                w.Write((ushort)2);
                w.Write((ushort)0);
                w.Write(1);
            });
            WriteSection(writer, "MATT0000", 88, 1, w =>
            {
                WriteFixedString(w, "test", 64);
                w.Write(new byte[24]);
            });
            WriteSection(writer, "REFSKELT", 120, 1, WriteRootBone);
            WriteSection(writer, "RAWWEIGHTS", 12, 3, w =>
            {
                for (var index = 0; index < 3; index++)
                {
                    w.Write(1f);
                    w.Write(index);
                    w.Write(0);
                }
            });
        }
        using (var writer = new BinaryWriter(File.Create(psa)))
        {
            WriteSection(writer, "ANIMHEAD", 0, 0, _ => { });
            WriteSection(writer, "BONENAMES", 120, 1, WriteRootBone);
            WriteSection(writer, "ANIMINFO", 168, 1, w =>
            {
                WriteFixedString(w, "move", 64);
                WriteFixedString(w, "test", 64);
                w.Write(1);
                w.Write(0);
                w.Write(0);
                w.Write(0);
                w.Write(0f);
                w.Write(1f);
                w.Write(30f);
                w.Write(0);
                w.Write(0);
                w.Write(1);
            });
            WriteSection(writer, "ANIMKEYS", 32, 1, w =>
            {
                WriteVector(w, 50, 0, 0);
                WriteVector(w, 0, 0, 0);
                w.Write(1f);
                w.Write(1f / 30f);
            });
            WriteSection(writer, "SCALEKEYS", 16, 1, w =>
            {
                WriteVector(w, 2, 3, 4);
                w.Write(1f / 30f);
            });
        }
        return (psk, psa);
    }

    private static void WriteBone(BinaryWriter writer)
    {
        WriteBone(writer, Quaternion.Identity);
    }

    private static void WriteBone(BinaryWriter writer, Quaternion rotation)
    {
        WriteFixedString(writer, "root", 64);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        WriteVector(writer, rotation.X, rotation.Y, rotation.Z);
        writer.Write(rotation.W);
        WriteVector(writer, 25, 0, 0);
        writer.Write(1f);
        WriteVector(writer, 1, 1, 1);
    }

    private static void WriteVector(BinaryWriter writer, float x, float y, float z)
    {
        writer.Write(x);
        writer.Write(y);
        writer.Write(z);
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int size)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        writer.Write(bytes);
        writer.Write(new byte[size - bytes.Length]);
    }

    private static void WriteSection(BinaryWriter writer, string name, int size, int count, Action<BinaryWriter> write)
    {
        WriteFixedString(writer, name, 20);
        writer.Write(0);
        writer.Write(size);
        writer.Write(count);
        write(writer);
    }
}
