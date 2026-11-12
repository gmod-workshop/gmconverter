using System.Numerics;
using GMConverter.Core.Textures;
using GMConverter.SDK.Materials;
using GMConverter.SourceEngine.Particles.Shaders;

namespace GMConverter.SourceEngine.Tests;

public sealed class MaterialGraphHlslTests
{
    [Fact]
    public void NodesBecomeStatementsWithBroadcastsAndSwizzles()
    {
        // The Kukri helix glow, abridged: a texture slid by parameter x, made grey, tinted by the particle.
        var graph = Graph(
        [
            new(MaterialGraphOp.TexCoord, []),
            MaterialGraphNode.Constant(new Vector4(1f, 3f, 0f, 0f), 2),
            new(MaterialGraphOp.Multiply, [0, 1]),
            new(MaterialGraphOp.ParticleParameters, []),
            new(MaterialGraphOp.Swizzle, [3]) { Components = "x" },
            MaterialGraphNode.Constant(new Vector4(0f, -1f, 0f, 0f), 2),
            new(MaterialGraphOp.Multiply, [4, 5]),
            new(MaterialGraphOp.Add, [2, 6]),
            new(MaterialGraphOp.TextureSample, [7]) { Texture = 0 },
            new(MaterialGraphOp.Swizzle, [8]) { Components = "xyz" },
            MaterialGraphNode.Constant(6f),
            new(MaterialGraphOp.Power, [9, 10]),
            MaterialGraphNode.Constant(new Vector4(0.3f, 0.59f, 0.11f, 0f), 3),
            new(MaterialGraphOp.Dot, [11, 12]),
            new(MaterialGraphOp.ParticleColor, []),
            new(MaterialGraphOp.Multiply, [13, 14]),
        ], emissive: 15, opacity: 14);

        var hlsl = MaterialGraphHlsl.Write(graph, MaterialBlendMode.Additive, "test");

        Assert.Contains("sampler Texture0 : register(s0);", hlsl);
        Assert.Contains("float2 n1 = float2(1.0, 3.0);", hlsl);
        Assert.Contains("float n4 = n3.x;", hlsl);
        Assert.Contains("float2 n6 = ((float2)n4) * n5;", hlsl);
        Assert.Contains("float4 n8 = tex2D(Texture0, n7);", hlsl);
        Assert.Contains("float3 n11 = pow(max(n9, 0), ((float3)n10));", hlsl);
        Assert.Contains("float n13 = dot(n11, n12);", hlsl);
        Assert.Contains("float4 n15 = ((float4)n13) * n14;", hlsl);
        Assert.Contains("float3 emissive = n15.xyz;", hlsl);
        Assert.Contains("float opacity = saturate(n14.x);", hlsl);
        Assert.Contains("FinalOutput(float4(emissive * opacity, 1)", hlsl);
    }

    [Fact]
    public void TranslucentMaterialsOutputOpacityAsAlpha()
    {
        var graph = Graph([new(MaterialGraphOp.ParticleColor, [])], emissive: 0, opacity: null);

        var hlsl = MaterialGraphHlsl.Write(graph, MaterialBlendMode.AlphaBlend, "test");

        Assert.Contains("float opacity = 1;", hlsl);
        Assert.Contains("FinalOutput(float4(emissive, opacity)", hlsl);
    }

    [Fact]
    public void MoreTexturesThanSamplersAreRefused()
    {
        var texture = new DefaultTextureFactory().FromRgba("t", 1, 1, [255, 255, 255, 255], hasAlpha: false);
        var graph = new MaterialGraph([new(MaterialGraphOp.TexCoord, [])], [.. Enumerable.Repeat(new MaterialGraphTexture(texture), 9)], 0, null);

        Assert.Throws<NotSupportedException>(() => MaterialGraphHlsl.Write(graph, MaterialBlendMode.Additive, "test"));
    }

    private static MaterialGraph Graph(MaterialGraphNode[] nodes, int emissive, int? opacity)
    {
        var texture = new DefaultTextureFactory().FromRgba("t", 1, 1, [255, 255, 255, 255], hasAlpha: false);
        return new MaterialGraph(nodes, [new MaterialGraphTexture(texture)], emissive, opacity);
    }
}
