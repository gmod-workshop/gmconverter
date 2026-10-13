using System.Numerics;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.SourceEngine.Exporters;

namespace GMConverter.SourceEngine.Tests;

public sealed class SourceBodyPartsTests
{
    [Fact]
    public void AModelWithoutBodyGroupsIsOneBody()
    {
        var model = Droid(bodyGroups: null);

        var part = Assert.Single(SourceBodyParts.Plan(model, "droid"));

        Assert.False(part.IsGroup);
        Assert.Equal("body", part.Name);
        Assert.Equal("droid.smd", part.Options.Single().SmdFileName);
        Assert.Equal(3, part.Options.Single().Meshes.Count);
    }

    [Fact]
    public void GroupedMeshesLeaveTheBodyAndEmptyOptionsAreBlank()
    {
        var model = Droid([new BodyGroup("Helmet", [new("On", [1]), new("Off", [])])]);

        var parts = SourceBodyParts.Plan(model, "droid");

        Assert.Equal(2, parts.Count);
        Assert.Equal(2, parts[0].Options.Single().Meshes.Count);
        Assert.True(parts[1].IsGroup);
        Assert.Equal("Helmet", parts[1].Name);
        Assert.Equal(["droid_helmet_on.smd", null], parts[1].Options.Select(option => option.SmdFileName));
    }

    [Fact]
    public void AGroupCalledBodyPushesTheAlwaysShownPartAside()
    {
        var model = Droid([new BodyGroup("body", [new("Plain", [0]), new("Coil", [2])])]);

        var parts = SourceBodyParts.Plan(model, "droid");

        Assert.Equal(["base", "body"], parts.Select(part => part.Name));
    }

    [Fact]
    public void EveryMeshInAGroupLeavesNoBody()
    {
        var model = Droid([new BodyGroup("body", [new("Plain", [0, 1]), new("Coil", [2])])]);

        Assert.True(Assert.Single(SourceBodyParts.Plan(model, "droid")).IsGroup);
    }

    [Fact]
    public void DefaultMeshesLeaveOutWhatOnlyOtherOptionsShow()
    {
        var model = Droid([new BodyGroup("body", [new("Plain", [0]), new("Coil", [2])])]);

        Assert.Equal([model.Meshes[0], model.Meshes[1]], model.DefaultMeshes);
        var ungrouped = Droid(bodyGroups: null);
        Assert.Same(ungrouped.Meshes, ungrouped.DefaultMeshes);
    }

    // Three one-triangle meshes: a plain body, a helmet and a Coil body.
    private static Model Droid(IReadOnlyList<BodyGroup>? bodyGroups)
    {
        static Mesh Triangle(string name, float x) => new(
            [new Vertex(new Vector3(x, 0, 0), Vector3.UnitZ, Vector2.Zero), new Vertex(new Vector3(x + 1, 0, 0), Vector3.UnitZ, Vector2.Zero), new Vertex(new Vector3(x, 1, 0), Vector3.UnitZ, Vector2.Zero)],
            [new Submesh("mat", [new Triangle(0, 1, 2)])],
            name);
        return new Model("droid", [Triangle("plain", 0), Triangle("helmet", 0), Triangle("coil", 5)], [new Material("mat")], BodyGroups: bodyGroups);
    }
}
