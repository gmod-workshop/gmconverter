using System.Numerics;
using GMConverter.SDK.Materials;
using GMConverter.UnrealEngine.Formats.UE3.Materials;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UE3LitMaterialTests
{
    [Fact]
    public void GgxMaterialsTranslateWithBranchesTheirValuesZeroOutLeftOut()
    {
        var (content, package) = Material(liveWorldPosition: false);

        var translated = UE3MaterialTranslator.Translate(content, new(package, package.Exports[0]), out var failure);

        Assert.Null(failure);
        var lit = Assert.IsType<MaterialGraphSurface>(translated!.Surface);
        Assert.Equal(MaterialShading.UnrealGgx, lit.Shading);
        Assert.Equal(new Vector4(0.8f, 0.5f, 0.2f, 0f), translated.Nodes[lit.BaseColor].Value);
        // Metallic is a parameter left at 0 times a Custom node; roughness lerps all the way to 0.3
        // from a world position: neither untranslatable node is needed.
        Assert.Equal(0f, translated.Nodes[lit.Metallic].Uniform);
        Assert.Equal(0.3f, translated.Nodes[lit.Roughness].Uniform);
        Assert.Equal(0.5f, translated.Nodes[lit.Specular].Uniform);
        Assert.Equal(MaterialGraphOp.SceneReflection, translated.Nodes[lit.Reflection!.Value].Op);
        Assert.Equal(MaterialGraphOp.CameraVector, translated.Nodes[lit.Normal!.Value].Op);
        // A first power keeps its base, sign and all.
        Assert.Equal(-2f, translated.Nodes[translated.Emissive!.Value].Uniform);
        Assert.Empty(translated.Textures);
        Assert.DoesNotContain(translated.Nodes, node => node.Op == MaterialGraphOp.Lerp);
        new MaterialGraph(translated.Nodes, [], translated.Emissive, translated.Opacity) { Surface = lit }.Validate();
    }

    [Fact]
    public void TheGraphFileKeepsTheLitSurface()
    {
        var (content, package) = Material(liveWorldPosition: false);
        var translated = UE3MaterialTranslator.Translate(content, new(package, package.Exports[0]), out _)!;
        var path = Path.Join(PskFixtures.CreateDirectory(), "M_Lit" + UE3MaterialGraphFile.Extension);

        UE3MaterialGraphFile.Export(content, translated, Path.GetDirectoryName(path)!, []).Write(path);
        var graph = UE3MaterialGraphFile.Read(path).ToGraph(_ => null)!;

        Assert.Equal(translated.Surface, graph.Surface);
        Assert.Equal(translated.Nodes.Count, graph.Nodes.Count);
        Assert.Equal(translated.Nodes.Select(node => (node.Op, node.Value, node.Size)), graph.Nodes.Select(node => (node.Op, node.Value, node.Size)));
    }

    [Fact]
    public void ALiveUntranslatableNodeFailsWithItsName()
    {
        var (content, package) = Material(liveWorldPosition: true);

        var translated = UE3MaterialTranslator.Translate(content, new(package, package.Exports[0]), out var failure);

        Assert.Null(translated);
        Assert.Equal("it uses a WorldPosition node", failure);
    }

    [Fact]
    public void UnknownLightingModelsAreNotGuessed()
    {
        var builder = new UE3PackageBuilder();
        builder.Add("M_Cloth", "Material", properties => properties.Enum("LightingModel", "EMaterialLightingModel", "MLM_GGXSmithSchlickWrappedDiffuseRimHighlight"));
        var (content, package) = builder.Read();

        Assert.Null(UE3MaterialTranslator.Translate(content, new(package, package.Exports[0]), out var failure));
        Assert.Equal("it is lit by MLM_GGXSmithSchlickWrappedDiffuseRimHighlight, whose shading isn't known", failure);
    }

    // M_Lit, lit by CrossFire HD's GGX: a constant colour; metallic = Shine (0) * a Custom node;
    // roughness = lerp(0.3, world position, alpha); specular 0.5; the camera vector as its normal; its
    // reflection probe sample (TextureSampleCubeLOD) as its reflection; emission (-2)^1. Alpha is 0,
    // or 0.5 when the world position should be live.
    private static (UE3GameContent, UE3Package) Material(bool liveWorldPosition)
    {
        var builder = new UE3PackageBuilder();
        const int material = 1;
        int Expression(string className, Action<UE3PackageBuilder.Properties> properties) =>
            builder.Add($"{className}_{Guid.NewGuid():N}", "MaterialExpression" + className, properties, outer: material);

        builder.Add("M_Lit", "Material", properties =>
        {
            properties.Enum("LightingModel", "EMaterialLightingModel", "MLM_GGXSmithSchlickBurley");
            properties.Input("DiffuseColor", 2);
            properties.Input("Metallic", 3);
            properties.Input("Roughness", 6);
            properties.Input("SpecularColor", 10);
            properties.Input("Normal", 11);
            properties.Input("WorldReflection", 12);
            properties.Input("EmissiveColor", 13);
        });
        Expression("Constant3Vector", p =>
        {
            p.Float("R", 0.8f);
            p.Float("G", 0.5f);
            p.Float("B", 0.2f);
        }); // 2
        Expression("Multiply", p =>
        {
            p.Input("A", 4);
            p.Input("B", 5);
        }); // 3
        Expression("ScalarParameter", p => p.Name("ParameterName", "Shine")); // 4
        Expression("Custom", p => p.Enum("OutputType", "ECustomMaterialOutputType", "CMOT_Float1")); // 5
        Expression("LinearInterpolate", p =>
        {
            p.Input("A", 7);
            p.Input("B", 8);
            p.Input("Alpha", 9);
        }); // 6
        Expression("Constant", p => p.Float("R", 0.3f)); // 7
        Expression("WorldPosition", _ => { }); // 8
        Expression("Constant", p => p.Float("R", liveWorldPosition ? 0.5f : 0f)); // 9
        Expression("Constant", p => p.Float("R", 0.5f)); // 10
        Expression("CameraVector", _ => { }); // 11
        Expression("TextureSampleCubeLOD", _ => { }); // 12
        Expression("Power", p =>
        {
            p.Input("Base", 14);
            p.Input("Exponent", 15);
        }); // 13
        Expression("Constant", p => p.Float("R", -2f)); // 14
        Expression("Constant", p => p.Float("R", 1f)); // 15
        return builder.Read();
    }
}
