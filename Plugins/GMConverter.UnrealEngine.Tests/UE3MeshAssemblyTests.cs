using System.Numerics;
using GMConverter.UnrealEngine.Formats.UE3.Meshes;
using GMConverter.UnrealEngine.Profiles.CrossFireHD;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UE3MeshAssemblyTests
{
    [Fact]
    public void AttachedPartsHangFromTheBoneAndMoveIntoTheBodysSpace()
    {
        // The body's hand bone sits 10 units up and is turned a quarter turn about Z.
        var quarterTurn = Quaternion.Conjugate(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2));
        var body = Mesh("Arms", [new UE3MeshBone("b_root", 0, Quaternion.Identity, Vector3.Zero), new UE3MeshBone("b_RightWeapon", 0, quarterTurn, new Vector3(0, 0, 10))], new Vector3(1, 0, 0));
        var part = Mesh("Knife", [new UE3MeshBone("b_root", 0, Quaternion.Identity, Vector3.Zero)], new Vector3(1, 0, 0));

        var assembled = UE3MeshAssembly.Attach(body, part, "b_RightWeapon", "Weapon_");

        Assert.Equal(["b_root", "b_RightWeapon", "Weapon_b_root"], assembled.Bones.Select(bone => bone.Name));
        Assert.Equal(1, assembled.Bones[2].ParentIndex);
        // The part's vertex 1 unit along its X now points along the body's Y, above the hand.
        var moved = assembled.Vertices[3].Position;
        Assert.Equal(0f, moved.X, 4);
        Assert.Equal(1f, moved.Y, 4);
        Assert.Equal(10f, moved.Z, 4);
        Assert.Equal([2, 2, 2, 2], assembled.Vertices[3].Bones);
        Assert.Equal([0, 1, 2, 3, 4, 5], assembled.Indices);
        Assert.Equal(1, assembled.Sections[1].MaterialIndex);
        Assert.Equal(3, assembled.Sections[1].FirstIndex);
    }

    [Theory]
    [InlineData("AS_FPS_1PC_Kukri_SKIN_ESPORTS", "AS_FPS_1PC_Kukri")]
    [InlineData("AS_FPS_1WP_KUKRI_SKIN_MECHA_SAMGUKJI_ZHAOYUN", "AS_FPS_1WP_KUKRI")]
    [InlineData("AS_FPS_1PC_Kukri", null)]
    public void SkinSetsBuildOnTheBaseWeaponsSet(string setName, string? baseName)
    {
        Assert.Equal(baseName, CrossFireHDViewmodels.GetBaseSetName(setName));
    }

    [Theory]
    [InlineData("AS_FPS_1PC_Kukri", true)]
    [InlineData("AS_FPS_1PC_Kukri_Swim", false)]
    [InlineData("AS_FPS_1PC_Kukri_Swim_SKIN_MECHA_SAMGUKJI_ZHAOYUN", false)]
    [InlineData("AS_FPS_3PC_Kukri", false)]
    public void ViewmodelsUseTheDryLandFirstPersonSets(string setName, bool used)
    {
        Assert.Equal(used, CrossFireHDViewmodels.IsViewmodelSet(setName, CrossFireHDViewmodels.ArmsSetPrefix));
    }

    // One triangle weighted to the last bone, its first vertex at firstVertex.
    private static UE3SkeletalMesh Mesh(string name, UE3MeshBone[] bones, Vector3 firstVertex)
    {
        var bone = bones.Length - 1;
        UE3MeshVertex Vertex(Vector3 position) => new(position, Vector3.UnitZ, Vector2.Zero, [bone, bone, bone, bone], [255, 0, 0, 0]);
        return new UE3SkeletalMesh(name, bones, [0], [new UE3MeshSection(0, 0, 1)], [0, 1, 2], [Vertex(firstVertex), Vertex(Vector3.UnitY), Vertex(Vector3.UnitZ)], []);
    }
}
