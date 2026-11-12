using System.Numerics;
using GMConverter.Core.Textures;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.SourceEngine.Shading;

namespace GMConverter.SourceEngine.Tests;

public sealed class LitShadingTests
{
    private static readonly DefaultTextureFactory _textures = new();

    [Fact]
    public void NormalAndRoughnessAreComputedBeforeWhatReflects()
    {
        // The reflection vector is declared after the normal and roughness that it and the scene's
        // reflection need, even though its node comes first.
        var hlsl = LitMaterialHlsl.Write(LitGraph(), "test");

        var normal = hlsl.IndexOf("float3 normalTs = normalize(n4);", StringComparison.Ordinal);
        var roughness = hlsl.IndexOf("float roughness = n5;", StringComparison.Ordinal);
        var reflection = hlsl.IndexOf("float3 n0 = reflectionVector;", StringComparison.Ordinal);
        Assert.True(normal >= 0 && roughness > normal && reflection > roughness, hlsl);
        Assert.Contains("float3 n6 = sceneReflection;", hlsl);
        Assert.Contains("float4 n3 = tex2D(Texture0, n2);", hlsl);
        Assert.Contains("clip(n9 - 0.5);", hlsl);
        Assert.Contains("ShadeUnrealGgx(normal, view, In.world, In.atten,", hlsl);
        Assert.Contains("float3 baseColor = n3.xyz;", hlsl);
        Assert.Contains("baseColor, n7, n7, roughness, n6);", hlsl);
        Assert.Contains("color = g_Unlit.x > 0.5 ? baseColor : color;", hlsl);
        Assert.Contains("FinalOutput(float4(color + n8, 1)", hlsl);
        Assert.Contains("samplerCUBE SceneProbe : register(s6);", hlsl);
        Assert.Contains("sampler EnvironmentBrdf : register(s7);", hlsl);
    }

    [Fact]
    public void ANormalThatDependsOnItsOwnReflectionIsRefused()
    {
        var graph = LitGraph() with { Surface = LitGraph().Surface! with { Normal = 0 } };

        Assert.Throws<NotSupportedException>(() => LitMaterialHlsl.Write(graph, "test"));
    }

    [Fact]
    public void LitMaterialsLeaveTwoSamplersForTheProbeAndBrdf()
    {
        var texture = new MaterialGraphTexture(_textures.FromRgba("t", 1, 1, [255, 255, 255, 255], hasAlpha: false));
        var graph = LitGraph() with { Textures = [.. Enumerable.Repeat(texture, LitMaterialHlsl.MaximumTextures + 1)] };

        Assert.Throws<NotSupportedException>(() => LitMaterialHlsl.Write(graph, "test"));
    }

    [Fact]
    public void UnlitGraphsAreRefused()
    {
        var graph = LitGraph() with { Surface = null };

        Assert.Throws<NotSupportedException>(() => LitMaterialHlsl.Write(graph, "test"));
    }

    [Theory]
    // A mirror seen head-on reflects everything (A = 1, B = 0); seen edge-on, Fresnel moves it to B.
    [InlineData(0, EnvironmentBrdf.Size - 1, 0.97f, 1.0f, 0f, 0.01f)]
    [InlineData(0, 0, 0f, 0.6f, 0.3f, 1f)]
    public void TheEnvironmentBrdfHasTheSplitSumsLimits(int column, int row, float minA, float maxA, float minB, float maxB)
    {
        var (a, b) = EnvironmentBrdf.Integrate(column, row);

        Assert.InRange(a, minA, maxA);
        Assert.InRange(b, minB, maxB);
    }

    [Fact]
    public void TheEnvironmentBrdfNeverReflectsMoreThanItReceives()
    {
        for (var row = 0; row < EnvironmentBrdf.Size; row += 9)
        {
            for (var column = 0; column < EnvironmentBrdf.Size; column += 9)
            {
                var (a, b) = EnvironmentBrdf.Integrate(column, row);
                Assert.InRange(a + b, 0f, 1.01f);
            }
        }
    }

    [Fact]
    public void ModelsRegisterTheirLitMaterialsWithTheRuntime()
    {
        var output = Path.Join(Path.GetTempPath(), "GMConverter.ShadingTests", Guid.NewGuid().ToString("N"));
        var model = new Model("knife", [], [new Material("MI_Knife", graph: LitGraph()), new Material("Glove")]);
        List<string> warnings = [];

        var files = GarrysModShading.Write(model, "weapons/v_knife.mdl", output, Path.Join(output, "no-kit"), warnings);

        var registration = File.ReadAllText(Path.Join(output, "lua", "autorun", "client", "gmconverter_shading_v_knife.lua"));
        Assert.Contains("GMConverterFX.RegisterShading(\"models/weapons/v_knife.mdl\", {", registration);
        Assert.Matches("\\[\"mi_knife\"\\] = \\{ shader = \"gmc_lit_[0-9a-f]{10}_ps30\", vertexShader = \"gmc_lit_[0-9a-f]{10}_vs30\", textures = \\{ \\{ \"gmconverter/shading/v_knife/diffuse\", false \\} \\} \\}", registration);
        Assert.DoesNotContain("glove", registration, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Join(output, "materials", "gmconverter", "shading", "environment_brdf.vtf")));
        Assert.True(File.Exists(Path.Join(output, "materials", "gmconverter", "shading", "v_knife", "diffuse.vtf")));
        Assert.True(File.Exists(Path.Join(output, "lua", "autorun", "client", "gmconverter_fx.lua")));
        Assert.Single(Directory.GetFiles(Path.Join(output, "shaders", "src"), "*_ps30.hlsl"));
        // Without a shader kit the HLSL is still written, with a warning.
        Assert.Contains(warnings, warning => warning.Contains("weren't compiled", StringComparison.Ordinal));
        Assert.Contains(files, file => file.EndsWith("gmconverter_shading_v_knife.lua", StringComparison.Ordinal));
    }

    [Fact]
    public void ModelsWithoutLitMaterialsWriteNothing()
    {
        var output = Path.Join(Path.GetTempPath(), "GMConverter.ShadingTests", Guid.NewGuid().ToString("N"));
        var model = new Model("prop", [], [new Material("Plain")]);

        Assert.Empty(GarrysModShading.Write(model, "props/prop.mdl", output, null, []));
        Assert.False(Directory.Exists(output));
    }

    // Base colour from a texture, a normal, roughness and metallic from constants, the scene's
    // reflection as is, emission and a cut-out; node 0 (the reflection vector) is only read by emission.
    private static MaterialGraph LitGraph()
    {
        var texture = new MaterialGraphTexture(_textures.FromRgba("diffuse", 1, 1, [200, 100, 50, 255], hasAlpha: false));
        MaterialGraphNode[] nodes =
        [
            new(MaterialGraphOp.ReflectionVector, []),
            new(MaterialGraphOp.CameraVector, []),
            new(MaterialGraphOp.TexCoord, []),
            new(MaterialGraphOp.TextureSample, [2]) { Texture = 0 },
            MaterialGraphNode.Constant(new Vector4(0f, 0f, 1f, 0f), 3),
            MaterialGraphNode.Constant(0.3f),
            new(MaterialGraphOp.SceneReflection, []),
            MaterialGraphNode.Constant(1f),
            new(MaterialGraphOp.Multiply, [0, 1]),
            new(MaterialGraphOp.Swizzle, [3]) { Components = "w" },
        ];
        return new MaterialGraph(nodes, [texture], Emissive: 8, Opacity: 9)
        {
            Surface = new MaterialGraphSurface(MaterialShading.UnrealGgx, BaseColor: 3, Metallic: 7, Specular: 7, Roughness: 5, Normal: 4, Reflection: 6),
        };
    }
}
