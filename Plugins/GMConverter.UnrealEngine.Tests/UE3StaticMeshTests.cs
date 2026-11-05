using System.Numerics;
using GMConverter.UnrealEngine.Formats.UE3.Meshes;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UE3StaticMeshTests
{
    [Fact]
    public void StaticMeshesBecomeOneBoneMeshesWithASlotPerMaterial()
    {
        var mesh = new UE3StaticMesh(
            "SM_Crate",
            [Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.One],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [Vector2.Zero, Vector2.UnitX, Vector2.UnitY, Vector2.One],
            [new UE3StaticMeshSection(-7, [0, 1, 2]), new UE3StaticMeshSection(-9, [1, 3, 2]), new UE3StaticMeshSection(-7, [2, 3, 0])]);

        var skeletal = mesh.ToSkeletalMesh();

        Assert.Equal("root", Assert.Single(skeletal.Bones).Name);
        Assert.Equal([-7, -9], skeletal.Materials);
        Assert.Equal([(0, 0, 1), (1, 3, 1), (0, 6, 1)], skeletal.Sections.Select(section => (section.MaterialIndex, section.FirstIndex, section.TriangleCount)));
        Assert.Equal([0, 1, 2, 1, 3, 2, 2, 3, 0], skeletal.Indices);
        Assert.All(skeletal.Vertices, vertex => Assert.Equal(255, vertex.Weights[0]));
    }
}
