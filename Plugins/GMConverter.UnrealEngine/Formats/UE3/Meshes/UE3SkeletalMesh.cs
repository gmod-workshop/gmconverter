using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Formats.UE3.Packages;
using GMConverter.UnrealEngine.Formats.UE3.Textures;

namespace GMConverter.UnrealEngine.Formats.UE3.Meshes;

/// <summary>
/// A UE3 SkeletalMesh's reference skeleton, materials, sockets and most detailed LOD, read from
/// its GPU skin vertex buffer.
/// </summary>
internal sealed record UE3SkeletalMesh(
    string Name,
    IReadOnlyList<UE3MeshBone> Bones,
    IReadOnlyList<int> Materials,
    IReadOnlyList<UE3MeshSection> Sections,
    IReadOnlyList<int> Indices,
    IReadOnlyList<UE3MeshVertex> Vertices,
    IReadOnlyList<UE3MeshSocket> Sockets)
{
    // GPU skin vertices and chunk-based render data settled at version 493.
    private const int _minimumVersion = 493;
    private const int _influencesPerVertex = 4;

    public static UE3SkeletalMesh Read(UE3Package package, UE3PackageExport export)
    {
        var version = package.Summary.FileVersion;
        if (version < _minimumVersion)
        {
            throw new GMConverterException($"UE3 skeletal mesh {export.ObjectName}: package version {version} is older than this reader supports.");
        }

        var (archive, properties) = package.OpenObject(export);
        archive.Skip(28); // bounds: origin, box extent, sphere radius
        var materials = archive.ReadArray(item => item.ReadInt32());
        archive.Skip(12 + 12); // mesh origin, rotation origin
        // UE3 stores every reference bone but the root with its rotation's W negated relative to the
        // animation keys; flip it back so mesh and animations share one convention.
        var bones = archive.ReadArray(item => ReadBone(item, version))
            .Select((bone, index) => index == 0 ? bone : bone with { Rotation = bone.Rotation with { W = -bone.Rotation.W } })
            .ToList();
        archive.ReadInt32(); // skeletal depth
        if (archive.ReadCount() == 0)
        {
            throw new GMConverterException($"UE3 skeletal mesh {export.ObjectName} has no LODs.");
        }

        var (sections, indices, chunks, vertices) = ReadLod(archive, version, export.ObjectName);
        var mapped = MapBones(vertices, chunks, bones.Count);
        return new UE3SkeletalMesh(export.ObjectName, bones, materials, sections, indices, mapped, ReadSockets(package, properties));
    }

    private static UE3MeshBone ReadBone(UE3Archive archive, int version)
    {
        var name = archive.ReadName();
        archive.ReadUInt32(); // flags
        var rotation = archive.ReadQuaternion();
        var position = archive.ReadVector3();
        archive.ReadInt32(); // child count
        var parent = archive.ReadInt32();
        if (version >= 515)
        {
            archive.ReadInt32(); // bone colour
        }

        return new UE3MeshBone(name, parent, rotation, position);
    }

    private static (List<UE3MeshSection>, int[], List<Chunk>, List<RawVertex>) ReadLod(UE3Archive archive, int version, string meshName)
    {
        var sections = archive.ReadArray(item =>
        {
            var material = item.ReadInt16();
            item.ReadInt16(); // chunk
            var firstIndex = item.ReadInt32();
            var triangles = version >= 806 ? item.ReadInt32() : item.ReadUInt16();
            if (version >= 599)
            {
                item.ReadByte();
            }

            return new UE3MeshSection(material, firstIndex, triangles);
        });

        var indices = ReadIndexBuffer(archive, version);
        if (version < 686)
        {
            archive.ReadArray(item => item.ReadUInt16());
        }

        archive.ReadArray(item => item.ReadInt16()); // active bones
        if (version < 686)
        {
            archive.ReadArray(item => item.ReadByte());
        }

        var chunks = archive.ReadArray(item => ReadChunk(item, version));
        archive.ReadInt32(); // size
        archive.ReadInt32(); // vertex count
        if (version < 686)
        {
            archive.Skip(archive.ReadCount() * 16); // edges
        }

        archive.ReadArray(item => item.ReadByte()); // required bones
        UE3BulkData.Read(archive); // raw point indices
        var uvSets = version >= 709 ? archive.ReadInt32() : 1;
        var vertices = ReadGpuVertices(archive, version, uvSets);
        if (vertices.Count == 0)
        {
            throw new GMConverterException($"UE3 skeletal mesh {meshName} has no GPU skin vertices.");
        }

        return (sections, indices, chunks, vertices);
    }

    private static int[] ReadIndexBuffer(UE3Archive archive, int version)
    {
        var itemSize = 2;
        if (version >= 806)
        {
            archive.ReadInt32();
            itemSize = archive.ReadByte();
        }

        var (elementSize, count, offset) = archive.ReadBulkArray();
        var indices = new int[count];
        var reader = new UE3Archive(archive.Data, archive.Names, offset);
        for (var i = 0; i < count; i++)
        {
            indices[i] = elementSize == 4 || itemSize == 4 ? reader.ReadInt32() : reader.ReadUInt16();
        }

        return indices;
    }

    private static Chunk ReadChunk(UE3Archive archive, int version)
    {
        var firstVertex = archive.ReadInt32();
        // CPU-skinned copies of the vertices; the GPU buffer below holds the same data.
        var uvSize = version >= 709 ? 4 * 8 : 8;
        var colorSize = version >= 710 ? 4 : 0;
        archive.Skip(archive.ReadCount() * (12 + 12 + uvSize + colorSize + 1));
        archive.Skip(archive.ReadCount() * (12 + 12 + uvSize + colorSize + _influencesPerVertex * 2));
        var bones = archive.ReadArray(item => (int)item.ReadInt16());
        var rigid = version >= 333 ? archive.ReadInt32() : 0;
        var soft = version >= 333 ? archive.ReadInt32() : 0;
        if (version >= 362)
        {
            archive.ReadInt32(); // max influences
        }

        return new Chunk(firstVertex, rigid + soft, bones);
    }

    private static List<RawVertex> ReadGpuVertices(UE3Archive archive, int version, int uvSets)
    {
        if (version >= 709)
        {
            uvSets = archive.ReadInt32();
        }

        var fullPrecisionUvs = archive.ReadInt32() != 0;
        var meshOrigin = Vector3.Zero;
        var meshExtension = Vector3.Zero;
        if (version >= 592)
        {
            archive.ReadInt32(); // packed position flag; element size says whether it was honoured
            meshExtension = archive.ReadVector3();
            meshOrigin = archive.ReadVector3();
        }

        var (elementSize, count, offset) = archive.ReadBulkArray();
        var normalBytes = version >= 494 ? 8 : 12;
        var uvBytes = uvSets * (fullPrecisionUvs ? 8 : 4);
        var positionBytes = elementSize - normalBytes - _influencesPerVertex * 2 - uvBytes;
        if (positionBytes is not (12 or 4))
        {
            throw new GMConverterException($"UE3 skeletal mesh vertices are {elementSize} bytes, which this reader doesn't recognise.");
        }

        List<RawVertex> vertices = new(count);
        var reader = new UE3Archive(archive.Data, archive.Names, offset);
        for (var i = 0; i < count; i++)
        {
            reader.Position = offset + i * elementSize;
            var position = Vector3.Zero;
            if (version < 592)
            {
                position = reader.ReadVector3();
            }

            reader.ReadUInt32(); // tangent
            if (normalBytes == 12)
            {
                reader.ReadUInt32(); // binormal, dropped from version 494
            }

            var normal = reader.ReadUInt32();
            var bones = new int[_influencesPerVertex];
            var weights = new byte[_influencesPerVertex];
            for (var j = 0; j < _influencesPerVertex; j++)
            {
                bones[j] = reader.ReadByte();
            }

            for (var j = 0; j < _influencesPerVertex; j++)
            {
                weights[j] = reader.ReadByte();
            }

            if (version >= 592)
            {
                position = positionBytes == 12 ? reader.ReadVector3() : UnpackPosition(reader.ReadUInt32(), meshOrigin, meshExtension);
            }

            var uv = fullPrecisionUvs
                ? new Vector2(reader.ReadSingle(), reader.ReadSingle())
                : new Vector2((float)reader.ReadHalf(), (float)reader.ReadHalf());
            vertices.Add(new RawVertex(position, UnpackNormal(normal), uv, bones, weights));
        }

        return vertices;
    }

    private static List<UE3MeshVertex> MapBones(List<RawVertex> vertices, List<Chunk> chunks, int boneCount)
    {
        List<UE3MeshVertex> mapped = new(vertices.Count);
        var chunk = 0;
        for (var i = 0; i < vertices.Count; i++)
        {
            while (chunk + 1 < chunks.Count && i >= chunks[chunk + 1].FirstVertex)
            {
                chunk++;
            }

            var chunkBones = chunks.Count > 0 ? chunks[chunk].Bones : [];
            var vertex = vertices[i];
            var bones = new int[_influencesPerVertex];
            for (var j = 0; j < _influencesPerVertex; j++)
            {
                var local = vertex.Bones[j];
                bones[j] = local < chunkBones.Count ? Math.Clamp(chunkBones[local], 0, boneCount - 1) : 0;
            }

            mapped.Add(new UE3MeshVertex(vertex.Position, vertex.Normal, vertex.Uv, bones, vertex.Weights));
        }

        return mapped;
    }

    private static List<UE3MeshSocket> ReadSockets(UE3Package package, UE3Properties properties)
    {
        List<UE3MeshSocket> sockets = [];
        foreach (var index in properties.GetObjectArray("Sockets"))
        {
            if (package.GetExport(index) is not { } export)
            {
                continue;
            }

            var socket = package.OpenObject(export).Properties;
            if (socket.GetName("SocketName") is not { } name || socket.GetName("BoneName") is not { } bone)
            {
                continue;
            }

            sockets.Add(new UE3MeshSocket(
                name,
                bone,
                socket.GetVector("RelativeLocation") ?? Vector3.Zero,
                socket.GetRotator("RelativeRotation") ?? (0, 0, 0),
                socket.GetVector("RelativeScale") ?? Vector3.One));
        }

        return sockets;
    }

    // "x / 127.5 - 1" per byte, as the shaders unpack it.
    private static Vector3 UnpackNormal(uint packed)
    {
        var normal = new Vector3(
            (packed & 0xFF) / 127.5f - 1f,
            ((packed >> 8) & 0xFF) / 127.5f - 1f,
            ((packed >> 16) & 0xFF) / 127.5f - 1f);
        return normal.LengthSquared() > 1e-6f ? Vector3.Normalize(normal) : Vector3.Zero;
    }

    // Consoles pack positions as 11/11/10-bit fractions of the mesh bounds.
    private static Vector3 UnpackPosition(uint packed, Vector3 origin, Vector3 extension)
    {
        var x = (int)(packed << 21) >> 21;
        var y = (int)(packed << 10) >> 21;
        var z = (int)packed >> 22;
        return new Vector3(x / 1023f, y / 1023f, z / 511f) * extension + origin;
    }

    private sealed record Chunk(int FirstVertex, int VertexCount, IReadOnlyList<int> Bones);

    private sealed record RawVertex(Vector3 Position, Vector3 Normal, Vector2 Uv, int[] Bones, byte[] Weights);
}
