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
    public void StaticMaskedSelfIlluminationBecomesSourceSelfIllumMask()
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

        var vmt = File.ReadAllText(Directory.GetFiles(output, "test.vmt", SearchOption.AllDirectories).Single());
        Assert.Contains("\"$selfillum\" \"1\"", vmt);
        Assert.Contains("\"$selfillummask\" \"gmconverter/test_illum\"", vmt);
        Assert.DoesNotContain("$detail", vmt);
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

    // UModel writes 32-bit TGAs whose image descriptor declares zero alpha bits even when the
    // fourth channel carries real opacity; decoders that trust the header discard it.
    private static void WriteUnlabeledAlphaTga(string path, byte alpha)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write([0, 0, 2]);
        writer.Write(new byte[9]);
        writer.Write((ushort)2);
        writer.Write((ushort)2);
        writer.Write((byte)32);
        writer.Write((byte)0x20);
        for (var index = 0; index < 4; index++)
        {
            writer.Write([240, 220, 80, alpha]);
        }
    }

    private static (string Psk, string Psa) WriteFixture()
    {
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
            WriteSection(writer, "REFSKELT", 120, 1, WriteBone);
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
            WriteSection(writer, "BONENAMES", 120, 1, WriteBone);
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
        WriteFixedString(writer, "root", 64);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        WriteVector(writer, 0, 0, 0);
        writer.Write(1f);
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
