using System.Numerics;
using GMConverter.Common;
using GMConverter.Geometry;

namespace GMConverter.Formats.MOW;

internal sealed class MOWPlyFile
{
    private static readonly byte[] Magic = "EPLYBNDS"u8.ToArray();
    private static readonly HashSet<uint> SupportedMaterialFormats =
    [
        0x0404, 0x0405, 0x0406, 0x0444,
        0x0504, 0x0544,
        0x0604, 0x0644,
        0x0704, 0x0705, 0x0744, 0x0745,
        0x0C14, 0x0C15, 0x0C54, 0x0C55,
        0x0F14, 0x0F15, 0x0F54
    ];
    private static readonly HashSet<uint> MaterialFormatsWithoutColor =
    [
        0x0404, 0x0405, 0x0406, 0x0444,
        0x0504, 0x0544,
        0x0C14, 0x0C15, 0x0C54, 0x0C55
    ];

    public string Path { get; }
    public Bounds Bounds { get; private set; } = new(Vector3.Zero, Vector3.Zero);
    public uint MeshInfo { get; private set; }
    public uint MaterialInfo { get; private set; }
    public string MaterialFile { get; private set; } = string.Empty;
    public IReadOnlyList<Vertex> Vertices => vertices;
    public IReadOnlyList<Triangle> Triangles => triangles;

    private readonly List<Vertex> vertices = [];
    private readonly List<Triangle> triangles = [];

    private MOWPlyFile(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
    }

    public static MOWPlyFile Read(string path)
    {
        var file = new MOWPlyFile(path);
        file.Read();
        return file;
    }

    private void Read()
    {
        using var stream = File.OpenRead(Path);
        using var reader = new BinaryReader(stream);

        var magic = reader.ReadBytes(8);
        if (!magic.SequenceEqual(Magic))
        {
            throw new GMConverterException($"Unsupported Men of War PLY file: {Path}");
        }

        Bounds = new Bounds(
            new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));

        while (stream.Position < stream.Length)
        {
            var entry = ReadAscii(reader, 4);
            switch (entry)
            {
                case "SKIN":
                    ReadSkinEntry(reader);
                    break;
                case "MESH":
                    ReadMeshEntry(reader);
                    break;
                case "VERT":
                    ReadVertexEntry(reader);
                    break;
                case "INDX":
                    ReadIndexEntry(reader);
                    return;
                default:
                    throw new GMConverterException($"Unsupported Men of War PLY entry '{entry}' in {Path}.");
            }
        }
    }

    private static void ReadSkinEntry(BinaryReader reader)
    {
        var skinCount = reader.ReadUInt32();
        for (var index = 0; index < skinCount; index++)
        {
            var nameLength = reader.ReadByte();
            reader.BaseStream.Seek(nameLength, SeekOrigin.Current);
        }
    }

    private void ReadMeshEntry(BinaryReader reader)
    {
        MeshInfo = reader.ReadUInt32();
        reader.BaseStream.Seek(4, SeekOrigin.Current);
        _ = reader.ReadUInt32(); // triangle count, repeated by INDX
        MaterialInfo = reader.ReadUInt32();

        if (!SupportedMaterialFormats.Contains(MaterialInfo))
        {
            throw new GMConverterException($"Unsupported Men of War material format 0x{MaterialInfo:X} in {Path}.");
        }

        if (!MaterialFormatsWithoutColor.Contains(MaterialInfo))
        {
            reader.BaseStream.Seek(4, SeekOrigin.Current);
        }

        var materialNameLength = reader.ReadByte();
        MaterialFile = ReadAscii(reader, materialNameLength);

        if (MeshInfo == 0x1118)
        {
            var count = reader.ReadByte();
            reader.BaseStream.Seek(count, SeekOrigin.Current);
        }
    }

    private void ReadVertexEntry(BinaryReader reader)
    {
        var vertexCount = reader.ReadUInt32();
        var vertexDescription = reader.ReadUInt32();
        var stride = checked((int)(vertexDescription & 0xFF));

        if (!IsSupportedVertexDescription(vertexDescription) || stride < 32)
        {
            throw new GMConverterException($"Unsupported Men of War vertex format 0x{vertexDescription:X8} in {Path}.");
        }

        var uvOffset = GetUvOffset(vertexDescription, stride);
        for (var index = 0; index < vertexCount; index++)
        {
            var record = reader.ReadBytes(stride);
            if (record.Length != stride)
            {
                throw new GMConverterException($"Unexpected end of Men of War PLY vertex data in {Path}.");
            }

            vertices.Add(new Vertex(
                new Vector3(ReadSingle(record, 0), ReadSingle(record, 4), ReadSingle(record, 8)),
                new Vector3(ReadSingle(record, 12), ReadSingle(record, 16), ReadSingle(record, 20)),
                new Vector2(ReadSingle(record, uvOffset), 1.0f - ReadSingle(record, uvOffset + 4))));
        }
    }

    private void ReadIndexEntry(BinaryReader reader)
    {
        var indexCount = reader.ReadUInt32();
        if (indexCount % 3 != 0)
        {
            throw new GMConverterException($"Men of War PLY index count is not divisible by 3 in {Path}.");
        }

        for (var index = 0; index < indexCount / 3; index++)
        {
            var i0 = reader.ReadUInt16();
            var i1 = reader.ReadUInt16();
            var i2 = reader.ReadUInt16();
            if (MaterialInfo is 0x0744 or 0x0C54)
            {
                triangles.Add(new Triangle(i0, i1, i2));
            }
            else
            {
                triangles.Add(new Triangle(i2, i1, i0));
            }
        }
    }

    private static bool IsSupportedVertexDescription(uint vertexDescription)
    {
        return vertexDescription is
            0x00010020 or 0x00010024 or 0x00010028 or 0x0001002C or 0x00010030 or 0x00010034 or 0x00010038 or
            0x00070020 or 0x00070024 or 0x00070028 or 0x0007002C or 0x00070030 or 0x00070034 or 0x00070038;
    }

    private static int GetUvOffset(uint vertexDescription, int stride)
    {
        return vertexDescription is 0x0007002C or 0x00070030 or 0x00070034 or 0x00070038
            ? 24
            : stride - 8;
    }

    private static string ReadAscii(BinaryReader reader, int count)
    {
        return System.Text.Encoding.ASCII.GetString(reader.ReadBytes(count));
    }

    private static float ReadSingle(ReadOnlySpan<byte> record, int offset)
    {
        return BitConverter.ToSingle(record[offset..(offset + 4)]);
    }
}
