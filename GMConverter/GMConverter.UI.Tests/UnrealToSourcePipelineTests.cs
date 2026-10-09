using System.Numerics;
using GMConverter.Core.Plugins;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;
using GMConverter.UnrealEngine.Tests;

namespace GMConverter.UI.Tests;

// Imports PSK fixtures with the Unreal Engine plugin and exports them with the Source Engine
// plugin, so these cover the sidecar-to-VMT path that neither engine's own tests can reach.
public sealed class UnrealToSourcePipelineTests
{
    private static readonly string[] _sourceBlendParameters = ["$additive", "$alphatest\"", "$translucent"];

    [Fact]
    public void SidecarUvScrollBecomesSourceTextureScrollProxy()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=fluid\nUvScroll=0,-0.2\n");
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "fluid.tga"), alpha: 255);

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
        _ = Record.Exception(() => PluginHost.Registry.GetExporter("mdl")!.Export(model, output, "triangle", new OptionValues(
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
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"),
            "Diffuse=fluid\nSelfIllumination=fluid\nSelfIlluminationMask=bubbles\nUvScroll=0.1,0\nEmissiveUvScroll=0.2,0\n");
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "fluid.tga"), alpha: 255);
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "bubbles.tga"), alpha: 128);

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
        _ = Record.Exception(() => PluginHost.Registry.GetExporter("mdl")!.Export(model, output, "triangle", new OptionValues(
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
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"),
            "Diffuse=panel\nSelfIllumination=panel\nSelfIlluminationMask=panel\n");
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 51);

        var model = PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory)));
        var material = Assert.Single(model.Materials);
        Assert.Null(material.EmissiveUvScrollRate);

        // Glow = colour (R80 G220 B240) x the mask's own alpha (51/255 = 0.2), per pixel.
        Assert.Equal([16, 44, 48, 255], material.EmissiveTexture!.GetRgbaPixels()[..4]);

        var output = Path.Join(directory, "mdl");
        var stubStudioMdl = Path.Join(directory, "cestudiomdl.exe");
        File.WriteAllText(stubStudioMdl, string.Empty);
        _ = Record.Exception(() => PluginHost.Registry.GetExporter("mdl")!.Export(model, output, "triangle", new OptionValues(
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

    [Theory]
    [InlineData("Blend=Masked\nAlphaRef=127\n", "\"$alphatest\" \"1\"", "\"$alphatestreference\" \"0.498\"")]
    [InlineData("Blend=Additive\n", "\"$additive\" \"1\"", null)]
    [InlineData("Blend=Translucent\n", "\"$translucent\" \"1\"", null)]
    public void SidecarBlendModeReachesSourceVmt(string blendLines, string expected, string? alsoExpected)
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=panel\nOpacity=panel\n" + blendLines);
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 200);

        var model = PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory)));
        var output = Path.Join(directory, "mdl");
        var stubStudioMdl = Path.Join(directory, "cestudiomdl.exe");
        File.WriteAllText(stubStudioMdl, string.Empty);
        _ = Record.Exception(() => PluginHost.Registry.GetExporter("mdl")!.Export(model, output, "triangle", new OptionValues(
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
    public void ScrollingSelfIlluminationColourBecomesSourceEmissiveBlend()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"),
            "Diffuse=panel\nSelfIllumination=flash\nSelfIlluminationMask=panel\n" +
            "SelfIlluminationColor=100,50,0\nSelfIlluminationColorOperation=Add\nSelfIlluminationUvScroll=0,0.3\n");
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 51, size: 4);
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "flash.tga"), alpha: 255, size: 2, blue: 0, green: 200, red: 100);

        var model = PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory)));
        var material = Assert.Single(model.Materials);

        // Colour = flash (R100 G200 B0) + constant (R100 G50 B0), clamped; it scrolls under the
        // fixed mask, and the static fallback is that colour x mask alpha 51/255.
        var layer = Assert.IsType<GMConverter.SDK.Materials.MaterialEmissiveLayer>(material.EmissiveLayer);
        Assert.Equal(new Vector2(0, 0.3f), layer.ScrollRate);
        Assert.Equal([200, 250, 0, 255], layer.Color.GetRgbaPixels()[..4]);
        Assert.Equal(51, layer.Mask.GetRgbaPixels()[3]);
        Assert.Equal([40, 50, 0, 255], material.EmissiveTexture!.GetRgbaPixels()[..4]);

        var output = Path.Join(directory, "mdl");
        var stubStudioMdl = Path.Join(directory, "cestudiomdl.exe");
        File.WriteAllText(stubStudioMdl, string.Empty);
        _ = Record.Exception(() => PluginHost.Registry.GetExporter("mdl")!.Export(model, output, "triangle", new OptionValues(
            new Dictionary<string, object?>
            {
                ["studioMdlPath"] = stubStudioMdl,
                ["buildMaterials"] = false
            })));

        var vmt = File.ReadAllText(Directory.GetFiles(output, "test.vmt", SearchOption.AllDirectories).Single());
        Assert.Contains("\"$selfillumtint\" \"[0 0 0]\"", vmt);
        Assert.Contains("\"$emissiveblendenabled\" \"1\"", vmt);
        Assert.Contains("\"$emissiveblendtexture\" \"gmconverter/test_glow\"", vmt);
        Assert.Contains("\"$emissiveblendbasetexture\" \"gmconverter/test_glowmask\"", vmt);
        Assert.Contains("\"$emissiveblendflowtexture\" \"gmconverter/gmconverter_flow\"", vmt);
        Assert.Contains("\"$emissiveblendscrollvector\" \"[0 0.3]\"", vmt);
        Assert.DoesNotContain("$detail", vmt);

        // The base alpha blacks out the masked area under $selfillum; the flow map's texels hold
        // their own UV so the pass samples the colour at the mesh UV.
        using var basePng = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(
            Directory.GetFiles(output, "test.png", SearchOption.AllDirectories).Single());
        Assert.Equal(51, basePng[0, 0].A);
        using var flowPng = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(
            Directory.GetFiles(output, "gmconverter_flow.png", SearchOption.AllDirectories).Single());
        Assert.Equal((0, 0), (flowPng[0, 0].R, flowPng[0, 0].G));
        Assert.Equal((128, 64), (flowPng[128, 64].R, flowPng[128, 64].G));
    }

    [Fact]
    public void SidecarVariantsBecomeSourceSkinFamilies()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"), "Diffuse=panel\nVariants=test_on,test_dest\n");
        File.WriteAllText(Path.Join(directory, "test_on.mat"), "Diffuse=panel\nSelfIllumination=panel\nSelfIlluminationMask=panel\n");
        File.WriteAllText(Path.Join(directory, "test_dest.mat"), "Diffuse=scorch\n");
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 51);
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "scorch.tga"), alpha: 255, blue: 0, green: 0, red: 200);

        var model = PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory)));

        Assert.Equal("test", Assert.Single(model.Materials).Name);
        Assert.Equal(["test_on", "test_dest"], model.Skins!.Select(skin => skin.Replacements["test"].Name));

        var output = Path.Join(directory, "mdl");
        var stubStudioMdl = Path.Join(directory, "cestudiomdl.exe");
        File.WriteAllText(stubStudioMdl, string.Empty);
        _ = Record.Exception(() => PluginHost.Registry.GetExporter("mdl")!.Export(model, output, "triangle", new OptionValues(
            new Dictionary<string, object?>
            {
                ["studioMdlPath"] = stubStudioMdl,
                ["buildMaterials"] = false
            })));

        var qc = File.ReadAllText(Directory.GetFiles(output, "triangle.qc", SearchOption.AllDirectories).Single());
        Assert.Contains("$texturegroup \"skinfamilies\"\n{\n    { \"test\" }\n    { \"test_on\" }\n    { \"test_dest\" }\n}", qc.ReplaceLineEndings("\n"));
        Assert.Contains("$selfillum", File.ReadAllText(Directory.GetFiles(output, "test_on.vmt", SearchOption.AllDirectories).Single()));
        Assert.Single(Directory.GetFiles(output, "test_dest.vmt", SearchOption.AllDirectories));
    }

    [Fact]
    public void SidecarUvTransformBecomesSourceTextureTransformProxies()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"),
            "Diffuse=panel\nUvScroll=0,0.2\nUvTransform=u=stretch,0.1,0.2,0;center=0.5,0.5;v=jitter,1,80,0;u=pan,9,9,9\n");
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 255);

        var model = PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory)));
        var transform = Assert.Single(model.Materials).UvTransform!;
        Assert.Equal(new Vector2(0.5f, 0.5f), transform.Center);
        Assert.Equal(new GMConverter.SDK.Materials.MaterialOscillation(GMConverter.SDK.Materials.MaterialOscillationKind.Stretch, 0.1f, 0.2f, 0f), transform.U);
        Assert.Equal(GMConverter.SDK.Materials.MaterialOscillationKind.Jitter, transform.V!.Kind);

        var vmt = ExportVmt(model, directory, "test.vmt");
        Assert.DoesNotContain("\"TextureScroll\"", vmt);
        Assert.Contains("\"$gmc_base_center\" \"[0.5 0.5]\"", vmt);
        // Stretch along U (period 1 / 0.2 s), the V scroll wrapped to [0, 1] plus jitter noise.
        Assert.Contains("\"sinemin\" \"0.9\"", vmt);
        Assert.Contains("\"sineperiod\" \"5\"", vmt);
        Assert.Contains("\"resultVar\" \"$gmc_base_scale[0]\"", vmt);
        Assert.Contains("\"WrapMinMax\"", vmt);
        Assert.Contains("\"UniformNoise\"", vmt);
        Assert.Contains("\"resultVar\" \"$gmc_base_translate[1]\"", vmt);
        Assert.Contains("\"resultVar\" \"$basetexturetransform\"", vmt);
    }

    [Fact]
    public void SidecarDetailLayerBecomesSourceDetailTexture()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"),
            "Diffuse=lightmap\nDetail=burlap\nDetailBlend=Multiply\nDetailUvTransform=scale=0.25,0.25\n");
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "lightmap.tga"), alpha: 255);
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "burlap.tga"), alpha: 255, blue: 200, green: 100, red: 50);

        var model = PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory)));
        var layer = Assert.Single(model.Materials).DetailLayer!;
        Assert.Equal(GMConverter.SDK.Materials.MaterialLayerBlend.Multiply, layer.Blend);
        Assert.Equal(new Vector2(0.25f, 0.25f), layer.UvTransform!.Scale);

        var vmt = ExportVmt(model, directory, "test.vmt");
        Assert.Contains("\"$detail\" \"gmconverter/test_detail\"", vmt);
        Assert.Contains("\"$detailblendmode\" \"0\"", vmt);
        Assert.Contains("\"$detailtexturetransform\" \"center 0 0 scale 0.25 0.25 rotate 0 translate 0 0\"", vmt);
        Assert.DoesNotContain("Proxies", vmt);

        // Source's mod2x doubles the product, so a plain multiply layer is written at half brightness.
        using var detailPng = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(
            Directory.GetFiles(Path.Join(directory, "mdl"), "test_detail.png", SearchOption.AllDirectories).Single());
        Assert.Equal((25, 50, 100), (detailPng[0, 0].R, detailPng[0, 0].G, detailPng[0, 0].B));
    }

    [Fact]
    public void FadingSelfIlluminationColourPulsesSourceEmissiveTint()
    {
        _ = ExporterOptionsTests.CreateViewModel();
        var (psk, _) = PskFixtures.WriteFixture();
        var directory = Path.GetDirectoryName(psk)!;
        File.WriteAllText(Path.Join(directory, "test.mat"),
            "Diffuse=panel\nSelfIlluminationMask=panel\nSelfIlluminationColor=255,0,0\n" +
            "SelfIlluminationColorOperation=Replace\nSelfIlluminationFade=0,255,0,0.5,0.25\n");
        PskFixtures.WriteUnlabeledAlphaTga(Path.Join(directory, "panel.tga"), alpha: 51);

        var model = PluginHost.Registry.GetImporter("psk")!
            .Parse(psk, new ModelParseOptions(1f, Materials: new MaterialResolveOptions(directory)));
        var material = Assert.Single(model.Materials);

        // A FadePeriod of 0.5 s is one way, so a full red -> green -> red cycle takes 1 s. The
        // static fallback is the first colour (red) x mask alpha 51/255.
        var pulse = material.EmissiveLayer!.Pulse!;
        Assert.Equal((new Vector3(1, 0, 0), new Vector3(0, 1, 0), 1f, 0.25f), (pulse.From, pulse.To, pulse.Period, pulse.Phase));
        Assert.Equal([51, 0, 0, 255], material.EmissiveTexture!.GetRgbaPixels()[..4]);

        var vmt = ExportVmt(model, directory, "test.vmt");
        Assert.Contains("\"$emissiveblendenabled\" \"1\"", vmt);
        Assert.Contains("\"resultVar\" \"$emissiveblendtint[0]\"", vmt);
        Assert.Contains("\"resultVar\" \"$emissiveblendtint[1]\"", vmt);
        Assert.Contains("\"sineperiod\" \"1\"", vmt);
    }

    private static string ExportVmt(Model model, string directory, string vmtName)
    {
        var output = Path.Join(directory, "mdl");
        var stubStudioMdl = Path.Join(directory, "cestudiomdl.exe");
        File.WriteAllText(stubStudioMdl, string.Empty);
        _ = Record.Exception(() => PluginHost.Registry.GetExporter("mdl")!.Export(model, output, "triangle", new OptionValues(
            new Dictionary<string, object?>
            {
                ["studioMdlPath"] = stubStudioMdl,
                ["buildMaterials"] = false
            })));
        return File.ReadAllText(Directory.GetFiles(output, vmtName, SearchOption.AllDirectories).Single());
    }
}
