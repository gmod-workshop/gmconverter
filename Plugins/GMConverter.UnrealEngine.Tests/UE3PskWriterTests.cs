using System.Numerics;
using GMConverter.UnrealEngine.Formats.PSK;
using GMConverter.UnrealEngine.Formats.UE3.Exporters;
using GMConverter.UnrealEngine.Formats.UE3.Meshes;
using GMConverter.UnrealEngine.Formats.UE3.Textures;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UE3PskWriterTests
{
    [Fact]
    public void MirroredTexturesFoldTheOtherTileBackIntoTheSquare()
    {
        // One triangle in the painted tile and one in the mirrored tile, sharing the corner at U = 1.
        var mesh = CreateMesh([new(0.2f, 0.1f), new(1f, 0.1f), new(0.2f, 0.9f), new(1.8f, 0.9f), new(1f, 0.9f)], [0, 1, 2, 1, 3, 4]);
        var path = Path.Join(PskFixtures.CreateDirectory(), "mesh.psk");

        UE3PskWriter.Write(path, mesh, ["M_Knife"], [(UE3TextureAddress.Mirror, UE3TextureAddress.Wrap)]);

        var psk = PSKFile.Read(path);
        Assert.All(psk.Wedges, wedge => Assert.InRange(wedge.U, 0f, 1f));
        // The mirrored triangle reads the texels it did in game: U 1.8 samples 0.2.
        var mirrored = psk.Faces[1].WedgeIndices.Select(wedge => psk.Wedges[wedge]).ToArray();
        Assert.Contains(mirrored, wedge => wedge.PointIndex == 3 && Math.Abs(wedge.U - 0.2f) < 1e-5f);
        // The shared corner lands on the same U either way, so it stays one wedge.
        Assert.Equal(5, psk.Wedges.Count);
    }

    [Fact]
    public void RepeatingTexturesKeepTheirUvs()
    {
        var mesh = CreateMesh([new(0.2f, 0.1f), new(1.5f, 0.1f), new(0.2f, 2.5f)], [0, 1, 2]);
        var path = Path.Join(PskFixtures.CreateDirectory(), "mesh.psk");

        UE3PskWriter.Write(path, mesh, ["M_Tiled"], [(UE3TextureAddress.Wrap, UE3TextureAddress.Wrap)]);

        Assert.Equal([0.2f, 1.5f, 0.2f], PSKFile.Read(path).Wedges.Select(wedge => wedge.U));
    }

    [Fact]
    public void BonesAndPointsUseUModelsMirroredSpace()
    {
        var mesh = CreateMesh([new(0, 0), new(1, 0), new(0, 1)], [0, 1, 2]);
        var path = Path.Join(PskFixtures.CreateDirectory(), "mesh.psk");

        UE3PskWriter.Write(path, mesh, ["M"], [(UE3TextureAddress.Wrap, UE3TextureAddress.Wrap)]);

        var psk = PSKFile.Read(path);
        Assert.Equal(new Vector3(1, -2, 3), psk.Points[0]);
        var bone = Assert.Single(psk.Bones);
        Assert.Equal(new Quaternion(0.1f, -0.2f, 0.3f, -0.9f), bone.Rotation);
        Assert.Equal(3, psk.VertexNormals.Count);
    }

    private static UE3SkeletalMesh CreateMesh(Vector2[] uvs, int[] indices)
    {
        var vertices = uvs.Select(uv => new UE3MeshVertex(new Vector3(1, 2, 3), Vector3.UnitZ, uv, [0, 0, 0, 0], [255, 0, 0, 0])).ToArray();
        return new UE3SkeletalMesh(
            "SK_Test",
            [new UE3MeshBone("b_Root", 0, new Quaternion(0.1f, 0.2f, 0.3f, 0.9f), Vector3.Zero)],
            [0],
            [new UE3MeshSection(0, 0, indices.Length / 3)],
            indices,
            vertices,
            []);
    }
}
