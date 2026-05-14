using System.Numerics;
using GMConverter.Common;

namespace GMConverter.Formats.Unreal;

internal sealed record UnrealStaticMesh(
    IReadOnlyList<UnrealStaticMeshVertex> Vertices,
    IReadOnlyList<UnrealMeshSection> Sections,
    IReadOnlyList<int> Indices,
    IReadOnlyList<IReadOnlyList<Vector2>> UvStreams,
    IReadOnlyList<int> MaterialReferences)
{
    public static UnrealStaticMesh Read(UnrealObjectReader reader)
    {
        var properties = ReadPrimitive(reader);

        var sections = reader.ReadArray(ReadSection(reader));
        reader.ReadBox();
        var vertices = ReadStaticVertexStream(reader);
        if (reader.FileVersion >= 155)
        {
            _ = reader.ReadInt32();
        }

        if (reader.FileVersion >= 149)
        {
            _ = reader.ReadInt32();
        }

        _ = ReadColorStream(reader);
        _ = ReadColorStream(reader);
        var uvStreams = reader.ReadArray(ReadUvStream(reader));
        var indices = ReadRawIndexBuffer(reader);
        _ = ReadRawIndexBuffer(reader);
        _ = reader.ReadObjectIndex();

        if (vertices.Count == 0 || indices.Count == 0)
        {
            throw new GMConverterException("UE2 static mesh did not contain vertices and indices.");
        }

        return new UnrealStaticMesh(vertices, sections, indices, uvStreams, properties.ObjectReferences("Materials.Material"));
    }

    public Vector2 GetUv(int vertexIndex)
    {
        return UvStreams.Count > 0 && vertexIndex < UvStreams[0].Count
            ? UvStreams[0][vertexIndex]
            : Vector2.Zero;
    }

    private static UnrealPropertyCollection ReadPrimitive(UnrealObjectReader reader)
    {
        var properties = reader.ReadProperties();
        reader.ReadBox();
        reader.ReadSphere();
        return properties;
    }

    private static Func<UnrealMeshSection> ReadSection(UnrealObjectReader reader)
    {
        return () =>
        {
            _ = reader.ReadInt32();
            var firstIndex = reader.ReadUInt16();
            var firstVertex = reader.ReadUInt16();
            var lastVertex = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            var numFaces = reader.ReadUInt16();
            return new UnrealMeshSection(firstIndex, firstVertex, lastVertex, numFaces);
        };
    }

    private static IReadOnlyList<UnrealStaticMeshVertex> ReadStaticVertexStream(UnrealObjectReader reader)
    {
        var vertices = reader.ReadArray(() => new UnrealStaticMeshVertex(reader.ReadVector3(), reader.ReadVector3()));
        _ = reader.ReadInt32();
        return vertices;
    }

    private static IReadOnlyList<int> ReadRawIndexBuffer(UnrealObjectReader reader)
    {
        var indices = reader.ReadUInt16Array();
        _ = reader.ReadInt32();
        return indices;
    }

    private static IReadOnlyList<int> ReadColorStream(UnrealObjectReader reader)
    {
        var colors = reader.ReadArray(reader.ReadInt32);
        _ = reader.ReadInt32();
        return colors;
    }

    private static Func<IReadOnlyList<Vector2>> ReadUvStream(UnrealObjectReader reader)
    {
        return () =>
        {
            var data = reader.ReadArray(reader.ReadVector2);
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            return data;
        };
    }
}

internal sealed record UnrealSkeletalMesh(
    IReadOnlyList<Vector3> Points,
    IReadOnlyList<UnrealMeshWedge> Wedges,
    IReadOnlyList<UnrealMeshTriangle> Triangles,
    IReadOnlyList<UnrealMeshInfluence> Influences,
    IReadOnlyList<UnrealMeshBone> Bones,
    IReadOnlyList<int> MaterialReferences,
    IReadOnlyList<int> AnimationReferences)
{
    public static UnrealSkeletalMesh Read(UnrealObjectReader reader)
    {
        reader.SkipProperties();
        reader.ReadBox();
        reader.ReadSphere();

        var version = reader.ReadInt32();
        _ = reader.ReadInt32();
        _ = reader.ReadArray(reader.ReadUInt32);
        var textures = reader.ReadArray(reader.ReadObjectIndex);
        _ = reader.ReadVector3();
        _ = reader.ReadVector3();
        _ = ReadRotator(reader);
        _ = reader.ReadArray(reader.ReadUInt16);
        _ = reader.ReadArray(ReadMeshFace(reader));
        _ = reader.ReadArray(reader.ReadUInt16);
        _ = reader.ReadArray(ReadMeshWedge(reader));
        var materials = reader.ReadArray(ReadMeshMaterial(reader));
        _ = reader.ReadSingle();
        _ = reader.ReadSingle();
        _ = reader.ReadSingle();
        _ = reader.ReadInt32();
        _ = reader.ReadSingle();
        _ = reader.ReadSingle();

        if (version >= 3)
        {
            _ = reader.ReadInt32();
            _ = reader.ReadObjectIndex();
            _ = reader.ReadVector3();
            _ = ReadRotator(reader);
            _ = reader.ReadVector3();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
        }

        if (version >= 4)
        {
            _ = reader.ReadSingle();
        }

        if (version >= 7)
        {
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
        }
        _ = reader.ReadArray(reader.ReadVector3);
        var bones = reader.ReadArray(ReadBone(reader)).Select((bone, index) => bone with { Index = index }).ToArray();

        if (reader.FileVersion >= 142)
        {
            for (var i = 0; i < bones.Length; i++)
            {
                var rotation = bones[i].Rotation;
                bones[i] = bones[i] with { Rotation = new Quaternion(-rotation.X, -rotation.Y, -rotation.Z, rotation.W) };
            }
        }

        IReadOnlyList<int> animationReferences;
        if (version >= 5)
        {
            animationReferences = reader.ReadArray(() =>
            {
                if (reader.FileVersion >= 151)
                {
                    _ = reader.ReadInt32();
                }

                return reader.ReadObjectIndex();
            });
        }
        else
        {
            animationReferences = [reader.ReadObjectIndex()];
        }
        _ = reader.ReadInt32();
        _ = reader.ReadArray(ReadWeightIndex(reader));
        _ = reader.ReadArray(() =>
        {
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            return 0;
        });
        if (reader.FileVersion >= 140)
        {
            _ = reader.ReadArray(() =>
            {
                _ = reader.ReadName();
                _ = reader.ReadName();
                reader.Skip(64);
                return 0;
            });
        }
        else
        {
            _ = reader.ReadArray(reader.ReadName);
            _ = reader.ReadArray(reader.ReadName);
            _ = reader.ReadArray(() =>
            {
                reader.Skip(48);
                return 0;
            });
        }

        if (version >= 6)
        {
            _ = reader.ReadInt32();
        }
        SkipLodModels(reader);
        if (version < 5)
        {
            _ = reader.ReadObjectIndex();
        }

        var points = reader.ReadLazyArray(reader.ReadVector3);
        var wedges = reader.ReadLazyArray(ReadMeshWedge(reader));
        var triangles = reader.ReadLazyArray(ReadTriangle(reader));
        var influences = reader.ReadLazyArray(ReadInfluence(reader));
        _ = reader.ReadLazyArray(reader.ReadUInt16);
        _ = reader.ReadLazyArray(reader.ReadUInt16);

        if (points.Count == 0 || wedges.Count == 0 || triangles.Count == 0)
        {
            throw new GMConverterException("UE2 skeletal mesh did not contain base mesh geometry.");
        }

        var materialReferences = materials
            .Select(material => material.TextureIndex >= 0 && material.TextureIndex < textures.Count ? textures[material.TextureIndex] : 0)
            .ToArray();
        if (materialReferences.Length == 0)
        {
            materialReferences = [.. textures];
        }

        return new UnrealSkeletalMesh(points, wedges, triangles, influences, bones, materialReferences, animationReferences);
    }

    private static void SkipLodModels(UnrealObjectReader reader)
    {
        var count = reader.ReadCompactIndex();
        if (count < 0)
        {
            throw new GMConverterException("UE2 skeletal mesh LOD count is invalid.");
        }

        for (var i = 0; i < count; i++)
        {
            if (reader.FileVersion >= 146)
            {
                _ = reader.ReadInt32();
            }

            _ = reader.ReadArray(reader.ReadUInt32);
            _ = reader.ReadArray(() =>
            {
                _ = reader.ReadVector3();
                _ = reader.ReadUInt32();
                return 0;
            });
            _ = reader.ReadInt32();
            _ = reader.ReadArray(ReadSkelSection(reader));
            _ = reader.ReadArray(ReadSkelSection(reader));
            _ = ReadRawIndexBuffer(reader);
            _ = ReadRawIndexBuffer(reader);
            _ = ReadSkinVertexStream(reader);
            _ = reader.ReadLazyArray(ReadInfluence(reader));
            _ = reader.ReadLazyArray(ReadMeshWedge(reader));
            _ = reader.ReadLazyArray(ReadMeshFace(reader));
            _ = reader.ReadLazyArray(reader.ReadVector3);
            _ = reader.ReadSingle();
            _ = reader.ReadSingle();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
        }
    }

    private static IReadOnlyList<int> ReadRawIndexBuffer(UnrealObjectReader reader)
    {
        var indices = reader.ReadUInt16Array();
        _ = reader.ReadInt32();
        return indices;
    }

    private static IReadOnlyList<int> ReadSkinVertexStream(UnrealObjectReader reader)
    {
        _ = reader.ReadInt32();
        _ = reader.ReadInt32();
        _ = reader.ReadInt32();
        return reader.ReadArray(() =>
        {
            _ = reader.ReadVector3();
            _ = reader.ReadVector3();
            _ = reader.ReadVector2();
            return 0;
        });
    }

    private static Func<UnrealMeshSection> ReadSkelSection(UnrealObjectReader reader)
    {
        return () =>
        {
            var materialIndex = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            var firstFace = reader.ReadUInt16();
            var numFaces = reader.ReadUInt16();
            return new UnrealMeshSection(firstFace * 3, 0, 0, numFaces, materialIndex);
        };
    }

    private static Func<UnrealMeshWedge> ReadMeshWedge(UnrealObjectReader reader)
    {
        return () => new UnrealMeshWedge(reader.ReadUInt16(), reader.ReadSingle(), reader.ReadSingle(), 0);
    }

    private static Func<UnrealMeshTriangle> ReadTriangle(UnrealObjectReader reader)
    {
        return () => new UnrealMeshTriangle(
            reader.ReadUInt16(),
            reader.ReadUInt16(),
            reader.ReadUInt16(),
            reader.ReadByte(),
            reader.ReadByte(),
            reader.ReadUInt32());
    }

    private static Func<UnrealMeshTriangle> ReadMeshFace(UnrealObjectReader reader)
    {
        return () =>
        {
            var wedge0 = reader.ReadUInt16();
            var wedge1 = reader.ReadUInt16();
            var wedge2 = reader.ReadUInt16();
            var materialIndex = reader.ReadUInt16();
            return new UnrealMeshTriangle(wedge0, wedge1, wedge2, materialIndex, 0, 1);
        };
    }

    private static Func<UnrealMeshMaterial> ReadMeshMaterial(UnrealObjectReader reader)
    {
        return () => new UnrealMeshMaterial(reader.ReadUInt32(), reader.ReadInt32());
    }

    private static Func<UnrealMeshBone> ReadBone(UnrealObjectReader reader)
    {
        return () =>
        {
            var name = reader.ReadName();
            var flags = reader.ReadUInt32();
            var rotation = reader.ReadQuaternion();
            var position = reader.ReadVector3();
            var length = reader.ReadSingle();
            var size = reader.ReadVector3();
            var numChildren = reader.ReadInt32();
            var parentIndex = reader.ReadInt32();
            return new UnrealMeshBone(0, name, flags, parentIndex, numChildren, position, rotation, length, size);
        };
    }

    private static Func<UnrealMeshInfluence> ReadInfluence(UnrealObjectReader reader)
    {
        return () => new UnrealMeshInfluence(reader.ReadSingle(), reader.ReadUInt16(), reader.ReadUInt16());
    }

    private static Func<int> ReadWeightIndex(UnrealObjectReader reader)
    {
        return () =>
        {
            _ = reader.ReadArray(reader.ReadUInt16);
            _ = reader.ReadInt32();
            return 0;
        };
    }

    private static (int Pitch, int Yaw, int Roll) ReadRotator(UnrealObjectReader reader)
    {
        return (reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
    }
}

internal sealed record UnrealStaticMeshVertex(Vector3 Position, Vector3 Normal);

internal sealed record UnrealMeshSection(int FirstIndex, int FirstVertex, int LastVertex, int NumFaces, int MaterialIndex = 0);

internal sealed record UnrealMeshWedge(int PointIndex, float U, float V, int MaterialIndex);

internal sealed record UnrealMeshTriangle(int Wedge0, int Wedge1, int Wedge2, int MaterialIndex, int AuxMaterialIndex, uint SmoothingGroups);

internal sealed record UnrealMeshInfluence(float Weight, int PointIndex, int BoneIndex);

internal sealed record UnrealMeshMaterial(uint PolyFlags, int TextureIndex);

internal sealed record UnrealMeshBone(
    int Index,
    string Name,
    uint Flags,
    int ParentIndex,
    int NumChildren,
    Vector3 Position,
    Quaternion Rotation,
    float Length,
    Vector3 Size);
