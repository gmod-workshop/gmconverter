using System.Numerics;
using System.Text;
using GMConverter.UnrealEngine.Formats.UE3.Meshes;
using GMConverter.UnrealEngine.Formats.UE3.Textures;

namespace GMConverter.UnrealEngine.Formats.UE3.Exporters;

/// <summary>
/// Writes a UE3 skeletal mesh as an ActorX PSK in the mirrored space UModel uses (Y negated,
/// bone rotations as (x, -y, z, -w), triangles wound the other way), so the PSK importer reads it
/// like any other Unreal export.
/// <para>
/// Formats like Source can only repeat textures, so a material whose texture mirrors past the
/// 0-1 square has each triangle's UVs folded back into it: a triangle in an odd tile is flipped.
/// That is exact for every triangle that stays inside one tile, as artists lay mirrored halves out.
/// </para>
/// </summary>
internal static class UE3PskWriter
{
    public static void Write(
        string path,
        UE3SkeletalMesh mesh,
        IReadOnlyList<string> materialNames,
        IReadOnlyList<(UE3TextureAddress X, UE3TextureAddress Y)> materialAddressing)
    {
        List<(int Vertex, Vector2 Uv)> wedges = [];
        Dictionary<(int Vertex, Vector2 Uv), int> wedgeLookup = [];
        List<(int A, int B, int C, int Material)> faces = [];
        foreach (var section in mesh.Sections)
        {
            var (addressX, addressY) = section.MaterialIndex < materialAddressing.Count
                ? materialAddressing[section.MaterialIndex]
                : (UE3TextureAddress.Wrap, UE3TextureAddress.Wrap);
            for (var triangle = 0; triangle < section.TriangleCount; triangle++)
            {
                var first = section.FirstIndex + triangle * 3;
                if (first + 2 >= mesh.Indices.Count)
                {
                    break;
                }

                int[] corners = [mesh.Indices[first], mesh.Indices[first + 1], mesh.Indices[first + 2]];
                var uvs = corners.Select(corner => mesh.Vertices[corner].Uv).ToArray();
                var centre = (uvs[0] + uvs[1] + uvs[2]) / 3f;
                var folded = uvs.Select(uv => new Vector2(Fold(uv.X, centre.X, addressX), Fold(uv.Y, centre.Y, addressY))).ToArray();
                var wedge = new int[3];
                for (var corner = 0; corner < 3; corner++)
                {
                    var key = (corners[corner], folded[corner]);
                    if (!wedgeLookup.TryGetValue(key, out wedge[corner]))
                    {
                        wedge[corner] = wedges.Count;
                        wedgeLookup[key] = wedge[corner];
                        wedges.Add(key);
                    }
                }

                faces.Add((wedge[0], wedge[1], wedge[2], section.MaterialIndex));
            }
        }

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        WriteChunkHeader(writer, "ACTRHEAD", 20100422, 0, 0);
        WriteChunkHeader(writer, "PNTS0000", 0, 12, mesh.Vertices.Count);
        foreach (var vertex in mesh.Vertices)
        {
            WriteMirrored(writer, vertex.Position);
        }

        WriteChunkHeader(writer, "VTXW0000", 0, 16, wedges.Count);
        foreach (var (vertex, uv) in wedges)
        {
            writer.Write(vertex);
            writer.Write(uv.X);
            writer.Write(uv.Y);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)0);
        }

        var face32 = wedges.Count > ushort.MaxValue;
        WriteChunkHeader(writer, face32 ? "FACE3200" : "FACE0000", 0, face32 ? 18 : 12, faces.Count);
        foreach (var (a, b, c, material) in faces)
        {
            foreach (var wedge in new[] { b, a, c })
            {
                if (face32)
                {
                    writer.Write(wedge);
                }
                else
                {
                    writer.Write((ushort)wedge);
                }
            }

            writer.Write((byte)Math.Clamp(material, 0, 255));
            writer.Write((byte)0);
            writer.Write(1);
        }

        WriteChunkHeader(writer, "MATT0000", 0, 88, materialNames.Count);
        for (var i = 0; i < materialNames.Count; i++)
        {
            WriteFixedString(writer, materialNames[i], 64);
            writer.Write(i);
            writer.Write(new byte[20]);
        }

        WriteChunkHeader(writer, "REFSKELT", 0, 120, mesh.Bones.Count);
        for (var i = 0; i < mesh.Bones.Count; i++)
        {
            var bone = mesh.Bones[i];
            WriteFixedString(writer, bone.Name, 64);
            writer.Write(0); // flags
            writer.Write(mesh.Bones.Count(child => child.ParentIndex == i && child != bone));
            writer.Write(bone.ParentIndex);
            WriteMirrored(writer, bone.Rotation);
            WriteMirrored(writer, bone.Position);
            writer.Write(0f);
            writer.Write(new byte[12]);
        }

        var influences = mesh.Vertices
            .SelectMany((vertex, index) => Enumerable.Range(0, vertex.Bones.Length)
                .Where(slot => vertex.Weights[slot] > 0)
                .Select(slot => (Weight: vertex.Weights[slot] / 255f, Point: index, Bone: vertex.Bones[slot])))
            .ToArray();
        WriteChunkHeader(writer, "RAWWEIGHTS", 0, 12, influences.Length);
        foreach (var (weight, point, bone) in influences)
        {
            writer.Write(weight);
            writer.Write(point);
            writer.Write(bone);
        }

        WriteChunkHeader(writer, "VTXNORMS", 0, 12, mesh.Vertices.Count);
        foreach (var vertex in mesh.Vertices)
        {
            WriteMirrored(writer, vertex.Normal);
        }
    }

    // Tile index taken from the triangle's centre so its corners on a tile edge stay together.
    private static float Fold(float value, float centre, UE3TextureAddress address)
    {
        if (address != UE3TextureAddress.Mirror)
        {
            return value;
        }

        var tile = MathF.Floor(centre);
        return ((int)tile & 1) == 0 ? value - tile : tile + 1 - value;
    }

    private static void WriteChunkHeader(BinaryWriter writer, string name, int typeFlag, int dataSize, int dataCount)
    {
        WriteFixedString(writer, name, 20);
        writer.Write(typeFlag);
        writer.Write(dataSize);
        writer.Write(dataCount);
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int byteLength)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var count = Math.Min(bytes.Length, byteLength - 1);
        writer.Write(bytes, 0, count);
        writer.Write(new byte[byteLength - count]);
    }

    private static void WriteMirrored(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(-value.Y);
        writer.Write(value.Z);
    }

    private static void WriteMirrored(BinaryWriter writer, Quaternion value)
    {
        writer.Write(value.X);
        writer.Write(-value.Y);
        writer.Write(value.Z);
        writer.Write(-value.W);
    }
}
