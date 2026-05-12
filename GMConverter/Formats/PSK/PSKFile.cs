using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using GMConverter.Common;

namespace GMConverter.Formats.PSK;

/// <summary>
/// Unreal ActorX PSK/PSKX file.
/// </summary>
internal sealed class PSKFile
{
    private static readonly Encoding _sectionNameEncoding = Encoding.ASCII;
    private static readonly Encoding _textEncoding = Encoding.UTF8;

    public List<Vector3> Points { get; } = [];
    public List<PSKWedge> Wedges { get; } = [];
    public List<PSKFace> Faces { get; } = [];
    public List<PSKMaterial> Materials { get; } = [];
    public List<PSKBone> Bones { get; } = [];
    public List<PSKWeight> Weights { get; } = [];
    public List<Vector3> VertexNormals { get; } = [];
    public int VertexColorCount { get; private set; }
    public int ExtraUvChannelCount { get; private set; }

    public static PSKFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var psk = new PSKFile();

        while (stream.Position < stream.Length)
        {
            var section = ReadSection(reader);
            var sectionDataLength = checked(section.DataSize * section.DataCount);

            switch (section.Name)
            {
                case "ACTRHEAD":
                    SkipSection(reader, section);
                    break;

                case "PNTS0000":
                    ReadRecords(reader, section, record => psk.Points.Add(ReadVector3(record, 0)));
                    break;

                case "VTXW0000":
                    ReadRecords(reader, section, record => psk.Wedges.Add(ReadWedge(record, psk.Points.Count)));
                    break;

                case "FACE0000":
                case "FACE3200":
                    ReadRecords(reader, section, record => psk.Faces.Add(ReadFace(record)));
                    break;

                case "MATT0000":
                    ReadRecords(reader, section, record => psk.Materials.Add(ReadMaterial(record)));
                    break;

                case "REFSKELT":
                    ReadRecords(reader, section, record => psk.Bones.Add(ReadBone(record)));
                    break;

                case "RAWWEIGHTS":
                    ReadRecords(reader, section, record => psk.Weights.Add(ReadWeight(record)));
                    break;

                case "VERTEXCOLOR":
                    psk.VertexColorCount = section.DataCount;
                    SkipSection(reader, section);
                    break;

                case "VTXNORMS":
                    ReadRecords(reader, section, record => psk.VertexNormals.Add(ReadVector3(record, 0)));
                    break;

                default:
                    if (section.Name.StartsWith("EXTRAUV", StringComparison.OrdinalIgnoreCase))
                    {
                        psk.ExtraUvChannelCount++;
                    }

                    reader.BaseStream.Seek(sectionDataLength, SeekOrigin.Current);
                    break;
            }
        }

        if (psk.Points.Count <= 65536)
        {
            for (var i = 0; i < psk.Wedges.Count; i++)
            {
                var wedge = psk.Wedges[i];
                psk.Wedges[i] = wedge with { PointIndex = wedge.PointIndex & 0xFFFF };
            }
        }

        return psk;
    }

    public string MaterialName(int materialIndex)
    {
        if (materialIndex >= 0 && materialIndex < Materials.Count)
        {
            return Materials[materialIndex].Name;
        }

        return materialIndex < 0 ? "default" : $"material_{materialIndex}";
    }

    private static PSKSection ReadSection(BinaryReader reader)
    {
        var nameBytes = reader.ReadBytes(20);
        if (nameBytes.Length != 20)
        {
            throw new GMConverterException("Unexpected end of PSK while reading section header.");
        }

        return new PSKSection(
            DecodeFixedString(nameBytes, _sectionNameEncoding),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32());
    }

    private static void ReadRecords(BinaryReader reader, PSKSection section, Action<byte[]> readRecord)
    {
        if (section.DataSize <= 0 || section.DataCount < 0)
        {
            throw new GMConverterException($"Invalid PSK section size for {section.Name}.");
        }

        for (var i = 0; i < section.DataCount; i++)
        {
            var record = reader.ReadBytes(section.DataSize);
            if (record.Length != section.DataSize)
            {
                throw new GMConverterException($"Unexpected end of PSK while reading {section.Name}.");
            }

            readRecord(record);
        }
    }

    private static void SkipSection(BinaryReader reader, PSKSection section)
    {
        reader.BaseStream.Seek(checked(section.DataSize * section.DataCount), SeekOrigin.Current);
    }

    private static PSKWedge ReadWedge(ReadOnlySpan<byte> record, int pointCount)
    {
        RequireRecordSize(record, 16, "VTXW0000");

        var pointIndex = ReadInt32(record, 0);
        if (pointCount <= 65536)
        {
            pointIndex &= 0xFFFF;
        }

        return new PSKWedge(
            pointIndex,
            ReadSingle(record, 4),
            ReadSingle(record, 8),
            record[12]);
    }

    private static PSKFace ReadFace(ReadOnlySpan<byte> record)
    {
        if (record.Length >= 18)
        {
            return new PSKFace(
                [ReadInt32(record, 0), ReadInt32(record, 4), ReadInt32(record, 8)],
                record[12],
                record[13],
                ReadInt32(record, 14));
        }

        RequireRecordSize(record, 12, "FACE0000");
        return new PSKFace(
            [ReadUInt16(record, 0), ReadUInt16(record, 2), ReadUInt16(record, 4)],
            record[6],
            record[7],
            ReadInt32(record, 8));
    }

    private static PSKMaterial ReadMaterial(ReadOnlySpan<byte> record)
    {
        RequireRecordSize(record, 88, "MATT0000");
        var originalName = DecodeFixedString(record[..64], _textEncoding);
        return new PSKMaterial(NameHelpers.SanitizeMaterialName(originalName), originalName);
    }

    private static PSKBone ReadBone(ReadOnlySpan<byte> record)
    {
        RequireRecordSize(record, 120, "REFSKELT");
        var name = DecodeFixedString(record[..64], _textEncoding);

        return new PSKBone(
            name,
            ReadInt32(record, 64),
            ReadInt32(record, 68),
            ReadInt32(record, 72),
            ReadQuaternion(record, 76),
            ReadVector3(record, 92),
            ReadSingle(record, 104),
            ReadVector3(record, 108));
    }

    private static PSKWeight ReadWeight(ReadOnlySpan<byte> record)
    {
        RequireRecordSize(record, 12, "RAWWEIGHTS");
        return new PSKWeight(
            ReadSingle(record, 0),
            ReadInt32(record, 4),
            ReadInt32(record, 8));
    }

    private static Vector3 ReadVector3(ReadOnlySpan<byte> record, int offset)
    {
        RequireRecordSize(record[offset..], 12, "Vector3");
        return new Vector3(
            ReadSingle(record, offset),
            ReadSingle(record, offset + 4),
            ReadSingle(record, offset + 8));
    }

    private static Quaternion ReadQuaternion(ReadOnlySpan<byte> record, int offset)
    {
        RequireRecordSize(record[offset..], 16, "Quaternion");
        return new Quaternion(
            ReadSingle(record, offset),
            ReadSingle(record, offset + 4),
            ReadSingle(record, offset + 8),
            ReadSingle(record, offset + 12));
    }

    private static void RequireRecordSize(ReadOnlySpan<byte> record, int minimumSize, string sectionName)
    {
        if (record.Length < minimumSize)
        {
            throw new GMConverterException($"{sectionName} record is too small. Expected at least {minimumSize} bytes, got {record.Length}.");
        }
    }

    private static string DecodeFixedString(ReadOnlySpan<byte> value, Encoding encoding)
    {
        var length = value.IndexOf((byte)0);
        if (length < 0)
        {
            length = value.Length;
        }

        var text = encoding.GetString(value[..length]).Trim();
        return string.IsNullOrWhiteSpace(text) ? "default" : text;
    }

    private static int ReadInt32(ReadOnlySpan<byte> value, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(value.Slice(offset, 4));
    }

    private static int ReadUInt16(ReadOnlySpan<byte> value, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(offset, 2));
    }

    private static float ReadSingle(ReadOnlySpan<byte> value, int offset)
    {
        return BitConverter.Int32BitsToSingle(ReadInt32(value, offset));
    }
}
