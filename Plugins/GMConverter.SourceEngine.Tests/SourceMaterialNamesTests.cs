using System.Numerics;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.SourceEngine.Materials;

namespace GMConverter.SourceEngine.Tests;

public sealed class SourceMaterialNamesTests
{
    [Fact]
    public void MakeSafeReplacesDotsInMaterialsSubmeshesAndSkins()
    {
        var model = new Model(
            "bottle",
            [Mesh("Glass.1001", "Label")],
            [new Material("Glass.1001", blendMode: MaterialBlendMode.AlphaBlend), new Material("Label")],
            Skins: [new MaterialSkin("broken", new Dictionary<string, Material> { ["Glass.1001"] = new Material("Glass.Broken") })]);

        var safe = SourceMaterialNames.MakeSafe(model);

        Assert.Equal(["Glass_1001", "Label"], safe.Materials.Select(material => material.Name));
        Assert.Equal(MaterialBlendMode.AlphaBlend, safe.Materials[0].BlendMode);
        Assert.Equal(["Glass_1001", "Label"], safe.Meshes[0].Submeshes.Select(submesh => submesh.MaterialName));
        var replacement = Assert.Single(safe.Skins!).Replacements.Single();
        Assert.Equal("Glass_1001", replacement.Key);
        Assert.Equal("Glass_Broken", replacement.Value.Name);
    }

    [Fact]
    public void MakeSafeKeepsRenamedMaterialsDistinctFromExistingNames()
    {
        var model = new Model("props", [Mesh("Wood.001", "Wood_001")], [new Material("Wood.001"), new Material("Wood_001")]);

        var safe = SourceMaterialNames.MakeSafe(model);

        Assert.Equal(["Wood_001_2", "Wood_001"], safe.Materials.Select(material => material.Name));
        Assert.Equal(["Wood_001_2", "Wood_001"], safe.Meshes[0].Submeshes.Select(submesh => submesh.MaterialName));
    }

    [Fact]
    public void MakeSafeReturnsModelsWithoutDotsUnchanged()
    {
        var model = new Model("plain", [Mesh("Body")], [new Material("Body")]);

        Assert.Same(model, SourceMaterialNames.MakeSafe(model));
    }

    private static Mesh Mesh(params string[] materialNames)
    {
        Vertex[] vertices = [new(Vector3.Zero, Vector3.UnitZ, Vector2.Zero), new(Vector3.UnitX, Vector3.UnitZ, Vector2.UnitX), new(Vector3.UnitY, Vector3.UnitZ, Vector2.UnitY)];
        return new Mesh(vertices, [.. materialNames.Select(name => new Submesh(name, [new Triangle(0, 1, 2)]))]);
    }
}
