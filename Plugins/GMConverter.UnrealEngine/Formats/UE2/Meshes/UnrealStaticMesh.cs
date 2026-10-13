using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Formats.UE2.Packages;

namespace GMConverter.UnrealEngine.Formats.UE2.Meshes;

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
