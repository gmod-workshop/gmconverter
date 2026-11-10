using System.Numerics;
using GMConverter.Core.Textures;
using GMConverter.SDK.Materials;

namespace GMConverter.UI.Tests;

public sealed class MaterialGraphTests
{
    [Fact]
    public void SizesBroadcastScalarsAndFollowSwizzlesAndAppends()
    {
        var graph = Graph(
            new MaterialGraphNode(MaterialGraphOp.TexCoord, []),
            MaterialGraphNode.Constant(new Vector4(1f, 3f, 0f, 0f), 2),
            new MaterialGraphNode(MaterialGraphOp.Multiply, [0, 1]),
            new MaterialGraphNode(MaterialGraphOp.TextureSample, [2]),
            new MaterialGraphNode(MaterialGraphOp.Swizzle, [3]) { Components = "xyz" },
            MaterialGraphNode.Constant(0.5f),
            new MaterialGraphNode(MaterialGraphOp.Multiply, [4, 5]),
            new MaterialGraphNode(MaterialGraphOp.Dot, [6, 4]),
            new MaterialGraphNode(MaterialGraphOp.Append, [2, 7]),
            new MaterialGraphNode(MaterialGraphOp.Multiply, [3, 4]));

        Assert.Equal([2, 2, 2, 4, 3, 1, 3, 1, 3, 3], Enumerable.Range(0, graph.Nodes.Count).Select(graph.SizeOf));
        graph.Validate();
    }

    [Fact]
    public void ValidationRejectsForwardReferencesAndMissingTextures()
    {
        Assert.Throws<InvalidOperationException>(() => Graph(new MaterialGraphNode(MaterialGraphOp.OneMinus, [0])).Validate());
        Assert.Throws<InvalidOperationException>(() => Graph(
            new MaterialGraphNode(MaterialGraphOp.TexCoord, []),
            new MaterialGraphNode(MaterialGraphOp.TextureSample, [0]) { Texture = 1 }).Validate());
        Assert.Throws<InvalidOperationException>(() => Graph(
            new MaterialGraphNode(MaterialGraphOp.TexCoord, []),
            new MaterialGraphNode(MaterialGraphOp.Swizzle, [0]) { Components = "xq" }).Validate());
        Assert.Throws<InvalidOperationException>(() => Graph(new MaterialGraphNode(MaterialGraphOp.Add, [])).Validate());
    }

    [Fact]
    public void ViewVectorsAndSceneReflectionsAreThreeComponents()
    {
        var graph = Graph(
            new MaterialGraphNode(MaterialGraphOp.CameraVector, []),
            new MaterialGraphNode(MaterialGraphOp.ReflectionVector, []),
            new MaterialGraphNode(MaterialGraphOp.SceneReflection, []),
            new MaterialGraphNode(MaterialGraphOp.Swizzle, [0]) { Components = "xy" },
            new MaterialGraphNode(MaterialGraphOp.TangentToWorld, [1]));

        Assert.Equal([3, 3, 3, 2, 3], Enumerable.Range(0, graph.Nodes.Count).Select(graph.SizeOf));
        graph.Validate();
    }

    [Fact]
    public void UniformIsAConstantsSingleValue()
    {
        Assert.Equal(0f, MaterialGraphNode.Constant(new Vector4(0f, 0f, 0f, 7f), 3).Uniform);
        Assert.Null(MaterialGraphNode.Constant(new Vector4(0f, 1f, 0f, 0f), 2).Uniform);
        Assert.Null(new MaterialGraphNode(MaterialGraphOp.TexCoord, []).Uniform);
    }

    [Fact]
    public void ValidationChecksTheSurfacesOutputs()
    {
        var graph = Graph(MaterialGraphNode.Constant(0.5f)) with
        {
            Surface = new MaterialGraphSurface(MaterialShading.UnrealGgx, 0, 0, 0, 0, Normal: 3, Reflection: null),
        };

        Assert.Throws<InvalidOperationException>(graph.Validate);
    }

    private static MaterialGraph Graph(params MaterialGraphNode[] nodes)
    {
        var texture = new DefaultTextureFactory().FromRgba("t", 1, 1, [255, 255, 255, 255], hasAlpha: false);
        return new MaterialGraph(nodes, [new MaterialGraphTexture(texture)], nodes.Length - 1, null);
    }
}
