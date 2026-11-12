using System.Numerics;
using GMConverter.Core.Textures;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Textures;
using GMConverter.SourceEngine.Materials;

namespace GMConverter.SourceEngine.Tests;

public sealed class SourceTranslucencySplitTests
{
    // 2x2 texture: the top row is solid, the bottom row half transparent.
    private static readonly Texture _halfGlass = new DefaultTextureFactory().FromRgba(
        "glass",
        2,
        2,
        [
            255, 255, 255, 255, 255, 255, 255, 255,
            255, 255, 255, 128, 255, 255, 255, 128,
        ],
        hasAlpha: true);

    [Fact]
    public void SplitMovesSolidTrianglesOntoAnOpaqueCopy()
    {
        // V runs up from the bottom of the image, so V near 1 samples the solid top row.
        var model = new Model(
            "bottle",
            [Quad("Glass", solidV: 0.9f, blendedV: 0.1f)],
            [new Material("Glass", diffuseTexture: _halfGlass, blendMode: MaterialBlendMode.AlphaBlend)]);

        var split = SourceTranslucencySplit.Split(model);

        Assert.Equal(["Glass", "Glass_opaque"], split.Materials.Select(material => material.Name));
        Assert.Equal(MaterialBlendMode.AlphaBlend, split.Materials[0].BlendMode);
        Assert.Equal(MaterialBlendMode.Opaque, split.Materials[1].BlendMode);
        Assert.Same(_halfGlass, split.Materials[1].DiffuseTexture);
        Assert.Collection(
            split.Meshes[0].Submeshes,
            blended => Assert.Equal(("Glass", 1), (blended.MaterialName, blended.Triangles.Count)),
            opaque => Assert.Equal(("Glass_opaque", 1), (opaque.MaterialName, opaque.Triangles.Count)));
    }

    [Fact]
    public void SplitMakesAFullySolidMaterialOpaqueUnderItsOwnName()
    {
        var model = new Model(
            "cork",
            [Quad("Cork", solidV: 0.9f, blendedV: 0.9f)],
            [new Material("Cork", diffuseTexture: _halfGlass)]);

        var split = SourceTranslucencySplit.Split(model);

        var material = Assert.Single(split.Materials);
        Assert.Equal(("Cork", MaterialBlendMode.Opaque), (material.Name, material.BlendMode));
        Assert.Equal(["Cork"], split.Meshes[0].Submeshes.Select(submesh => submesh.MaterialName));
    }

    [Fact]
    public void SplitLeavesAlphaTestedAndSkinnedMaterialsAlone()
    {
        var cutout = new Model(
            "leaf",
            [Quad("Leaf", solidV: 0.9f, blendedV: 0.1f)],
            [new Material("Leaf", diffuseTexture: _halfGlass, blendMode: MaterialBlendMode.AlphaTest)]);
        var skinned = new Model(
            "bottle",
            [Quad("Glass", solidV: 0.9f, blendedV: 0.1f)],
            [new Material("Glass", diffuseTexture: _halfGlass, blendMode: MaterialBlendMode.AlphaBlend)],
            Skins: [new MaterialSkin("broken", new Dictionary<string, Material> { ["Glass"] = new Material("Broken") })]);

        Assert.Same(cutout, SourceTranslucencySplit.Split(cutout));
        Assert.Same(skinned, SourceTranslucencySplit.Split(skinned));
    }

    // Two triangles side by side, each lying along one row of the texture.
    private static Mesh Quad(string materialName, float solidV, float blendedV)
    {
        Vertex[] vertices =
        [
            new(Vector3.Zero, Vector3.UnitZ, new Vector2(0.1f, solidV)),
            new(Vector3.UnitX, Vector3.UnitZ, new Vector2(0.9f, solidV)),
            new(Vector3.UnitY, Vector3.UnitZ, new Vector2(0.5f, solidV)),
            new(Vector3.Zero, Vector3.UnitZ, new Vector2(0.1f, blendedV)),
            new(Vector3.UnitX, Vector3.UnitZ, new Vector2(0.9f, blendedV)),
            new(Vector3.UnitY, Vector3.UnitZ, new Vector2(0.5f, blendedV)),
        ];
        return new Mesh(vertices, [new Submesh(materialName, [new Triangle(0, 1, 2), new Triangle(3, 4, 5)])]);
    }
}
