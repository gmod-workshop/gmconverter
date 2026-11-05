using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Formats.UE3.Packages;
using GMConverter.UnrealEngine.Formats.UE3.Textures;

namespace GMConverter.UnrealEngine.Formats.UE3.Meshes;

/// <summary>
/// A cooked UE3 StaticMesh's first LOD: positions (Unreal units and space), normals, the first UV
/// set, and triangles grouped by section, each with the material it draws. Read as CrossFire HD
/// (package version 872) stores it: bounds, body setup, compact kDOP collision tree, internal version,
/// a licensee block of four to eight ints whose layout isn't known, then the LODs. The block's length
/// is found by reading the first LOD after each candidate and keeping the one whose sections, buffers
/// and index count all agree.
/// </summary>
internal sealed record UE3StaticMesh(
    string Name,
    IReadOnlyList<Vector3> Positions,
    IReadOnlyList<Vector3> Normals,
    IReadOnlyList<Vector2> Uvs,
    IReadOnlyList<UE3StaticMeshSection> Sections)
{
    private const int _minimumVersion = 770;

    // Five is by far the most common; try it first.
    private static readonly int[] _licenseeBlockLengths = [5, 4, 6, 7, 8];

    public static UE3StaticMesh Read(UE3Package package, UE3PackageExport export)
    {
        if (package.Summary.FileVersion < _minimumVersion)
        {
            throw new GMConverterException($"UE3 static meshes before package version {_minimumVersion} aren't supported ({export.ObjectName} is {package.Summary.FileVersion}).");
        }

        var (archive, _) = package.OpenObject(export);
        archive.Skip(7 * 4); // bounds: origin, extent, radius
        archive.ReadInt32(); // body setup
        archive.Skip(6 * 4); // kDOP root bounds
        SkipBulkArray(archive); // kDOP nodes
        SkipBulkArray(archive); // kDOP triangles
        archive.ReadInt32(); // internal version
        var lodStart = archive.Position;
        GMConverterException? failure = null;
        foreach (var unknownInts in _licenseeBlockLengths)
        {
            try
            {
                return ReadLod(package, export, new UE3Archive(archive.Data, archive.Names, lodStart + (unknownInts * 4)));
            }
            catch (GMConverterException exception)
            {
                failure ??= exception;
            }
        }

        throw failure!;
    }

    private static UE3StaticMesh ReadLod(UE3Package package, UE3PackageExport export, UE3Archive archive)
    {
        var lods = archive.ReadInt32();
        if (lods <= 0 || lods > 16)
        {
            throw new GMConverterException($"UE3 static mesh {export.ObjectName} has an unexpected LOD count {lods}.");
        }

        UE3BulkData.Read(archive); // raw triangles, inline or in a separate block

        var sectionCount = archive.ReadInt32();
        if (sectionCount <= 0 || sectionCount > 256)
        {
            throw new GMConverterException($"UE3 static mesh {export.ObjectName} has an unexpected section count {sectionCount}.");
        }

        List<(int Material, int FirstIndex, int Triangles)> sections = [];
        for (var section = 0; section < sectionCount; section++)
        {
            var material = archive.ReadInt32();
            archive.Skip(3 * 4); // collision, old collision, shadow casting
            var firstIndex = archive.ReadInt32();
            var triangles = archive.ReadInt32();
            archive.Skip(3 * 4); // min/max vertex index, material index
            archive.Skip(archive.ReadInt32() * 8); // fragments
            archive.ReadByte();
            sections.Add((material, firstIndex, triangles));
        }

        archive.Skip(2 * 4); // position stride, count
        var positions = ReadBulkArray(archive, 12, reader => reader.ReadVector3());

        var texCoords = archive.ReadInt32();
        archive.Skip(2 * 4); // stride, count
        var fullPrecision = archive.ReadInt32() != 0;
        var uvBytes = (fullPrecision ? 8 : 4) * texCoords;
        List<Vector3> normals = [];
        List<Vector2> uvs = [];
        ReadBulkArray(archive, 8 + uvBytes, reader =>
        {
            reader.ReadInt32(); // tangent X
            normals.Add(UnpackNormal(reader.ReadUInt32()));
            uvs.Add(fullPrecision ? new Vector2(reader.ReadSingle(), reader.ReadSingle()) : new Vector2((float)reader.ReadHalf(), (float)reader.ReadHalf()));
            reader.Skip(uvBytes - (fullPrecision ? 8 : 4));
            return 0;
        });

        archive.ReadInt32(); // colour stride
        if (archive.ReadInt32() > 0)
        {
            SkipBulkArray(archive);
        }

        archive.ReadInt32(); // vertex count
        var indices = ReadBulkArray(archive, 2, reader => (int)reader.ReadUInt16());
        if (sections.Sum(section => section.Triangles) * 3 > indices.Count)
        {
            throw new GMConverterException($"UE3 static mesh {export.ObjectName}'s sections reach past its {indices.Count} indices.");
        }

        return new UE3StaticMesh(
            export.ObjectName,
            positions,
            normals,
            uvs,
            [.. sections.Select(section => new UE3StaticMeshSection(
                section.Material,
                [.. indices.Skip(section.FirstIndex).Take(section.Triangles * 3)]))]);
    }

    /// <summary>The mesh as a skeletal mesh with a single root bone, so it shares the skeletal mesh export path.</summary>
    public UE3SkeletalMesh ToSkeletalMesh()
    {
        var materials = Sections.Select(section => section.Material).Distinct().ToArray();
        List<int> indices = [];
        List<UE3MeshSection> sections = [];
        foreach (var section in Sections)
        {
            sections.Add(new UE3MeshSection(Array.IndexOf(materials, section.Material), indices.Count, section.Indices.Count / 3));
            indices.AddRange(section.Indices);
        }

        return new UE3SkeletalMesh(
            Name,
            [new UE3MeshBone("root", -1, Quaternion.Identity, Vector3.Zero)],
            materials,
            sections,
            indices,
            [.. Positions.Select((position, index) => new UE3MeshVertex(position, Normals[index], Uvs[index], [0, 0, 0, 0], [255, 0, 0, 0]))],
            []);
    }

    private static void SkipBulkArray(UE3Archive archive)
    {
        var size = archive.ReadInt32();
        var count = archive.ReadInt32();
        archive.Skip(size * count);
    }

    private static List<T> ReadBulkArray<T>(UE3Archive archive, int expectedSize, Func<UE3Archive, T> read)
    {
        var size = archive.ReadInt32();
        var count = archive.ReadInt32();
        if (size != expectedSize)
        {
            throw new GMConverterException($"UE3 static mesh array elements are {size} bytes, not the expected {expectedSize}.");
        }

        List<T> items = new(count);
        for (var index = 0; index < count; index++)
        {
            items.Add(read(archive));
        }

        return items;
    }

    // Packed normals store each component as a byte, 0..255 for -1..1.
    private static Vector3 UnpackNormal(uint packed)
    {
        static float Component(uint value) => (value / 127.5f) - 1f;
        var normal = new Vector3(Component(packed & 0xFF), Component((packed >> 8) & 0xFF), Component((packed >> 16) & 0xFF));
        return normal.LengthSquared() > 0 ? Vector3.Normalize(normal) : Vector3.UnitZ;
    }
}
