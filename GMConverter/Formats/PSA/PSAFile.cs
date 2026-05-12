using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using GMConverter.Common;

namespace GMConverter.Formats.PSA;

/// <summary>
/// Unreal ActorX PSA files.
/// </summary>
internal sealed class PSAFile
{
    private static readonly Encoding _sectionNameEncoding = Encoding.ASCII;
    private static readonly Encoding _textEncoding = Encoding.Latin1;

    public List<PSABone> Bones { get; } = [];
    public List<PSASequence> Sequences { get; } = [];
    public List<PSAKey> Keys { get; } = [];
    public List<PSAScaleKey> ScaleKeys { get; } = [];

    public static PSAFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var psa = new PSAFile();

        while (stream.Position < stream.Length)
        {
            var section = ReadSection(reader);
            var sectionDataLength = checked(section.DataSize * section.DataCount);

            switch (section.Name)
            {
                case "ANIMHEAD":
                    SkipSection(reader, section);
                    break;

                case "BONENAMES":
                    ReadRecords(reader, section, record => psa.Bones.Add(ReadBone(record)));
                    break;

                case "ANIMINFO":
                    ReadRecords(reader, section, record => psa.Sequences.Add(ReadSequence(record)));
                    FixCue4ParseFrameStartIndices(psa.Sequences);
                    break;

                case "ANIMKEYS":
                    ReadRecords(reader, section, record => psa.Keys.Add(ReadKey(record)));
                    break;

                case "SCALEKEYS":
                    ReadRecords(reader, section, record => psa.ScaleKeys.Add(ReadScaleKey(record)));
                    break;

                default:
                    reader.BaseStream.Seek(sectionDataLength, SeekOrigin.Current);
                    break;
            }
        }

        return psa;
    }

    public IReadOnlyList<PSAKey> GetSequenceKeys(PSASequence sequence)
    {
        if (Bones.Count == 0 || sequence.FrameCount <= 0)
        {
            return [];
        }

        var start = checked(sequence.FrameStartIndex * Bones.Count);
        var count = checked(sequence.FrameCount * Bones.Count);
        if (start < 0 || start >= Keys.Count || start + count > Keys.Count)
        {
            throw new GMConverterException($"PSA sequence '{sequence.Name}' key range is outside the ANIMKEYS data.");
        }

        return Keys.GetRange(start, count);
    }

    public IReadOnlyList<PSAScaleKey> GetSequenceScaleKeys(PSASequence sequence)
    {
        if (ScaleKeys.Count == 0 || Bones.Count == 0 || sequence.FrameCount <= 0)
        {
            return [];
        }

        var start = checked(sequence.FrameStartIndex * Bones.Count);
        var count = checked(sequence.FrameCount * Bones.Count);
        if (start < 0 || start >= ScaleKeys.Count || start + count > ScaleKeys.Count)
        {
            return [];
        }

        return ScaleKeys.GetRange(start, count);
    }

    private static PSASection ReadSection(BinaryReader reader)
    {
        var nameBytes = reader.ReadBytes(20);
        if (nameBytes.Length != 20)
        {
            throw new GMConverterException("Unexpected end of PSA while reading section header.");
        }

        return new PSASection(
            DecodeFixedString(nameBytes, _sectionNameEncoding),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32());
    }

    private static void ReadRecords(BinaryReader reader, PSASection section, Action<byte[]> readRecord)
    {
        if (section.DataSize <= 0 || section.DataCount < 0)
        {
            throw new GMConverterException($"Invalid PSA section size for {section.Name}.");
        }

        for (var i = 0; i < section.DataCount; i++)
        {
            var record = reader.ReadBytes(section.DataSize);
            if (record.Length != section.DataSize)
            {
                throw new GMConverterException($"Unexpected end of PSA while reading {section.Name}.");
            }

            readRecord(record);
        }
    }

    private static void SkipSection(BinaryReader reader, PSASection section)
    {
        reader.BaseStream.Seek(checked(section.DataSize * section.DataCount), SeekOrigin.Current);
    }

    private static PSABone ReadBone(ReadOnlySpan<byte> record)
    {
        RequireRecordSize(record, 120, "BONENAMES");
        return new PSABone(
            DecodeFixedString(record[..64], _textEncoding),
            ReadInt32(record, 64),
            ReadInt32(record, 68),
            ReadInt32(record, 72),
            ReadQuaternion(record, 76),
            ReadVector3(record, 92),
            ReadSingle(record, 104),
            ReadVector3(record, 108));
    }

    private static PSASequence ReadSequence(ReadOnlySpan<byte> record)
    {
        RequireRecordSize(record, 168, "ANIMINFO");
        return new PSASequence(
            DecodeFixedString(record[..64], _textEncoding),
            DecodeFixedString(record.Slice(64, 64), _textEncoding),
            ReadInt32(record, 128),
            ReadInt32(record, 132),
            ReadInt32(record, 136),
            ReadInt32(record, 140),
            ReadSingle(record, 144),
            ReadSingle(record, 148),
            ReadSingle(record, 152),
            ReadInt32(record, 156),
            ReadInt32(record, 160),
            ReadInt32(record, 164));
    }

    private static PSAKey ReadKey(ReadOnlySpan<byte> record)
    {
        RequireRecordSize(record, 32, "ANIMKEYS");
        return new PSAKey(
            ReadVector3(record, 0),
            ReadQuaternion(record, 12),
            ReadSingle(record, 28));
    }

    private static PSAScaleKey ReadScaleKey(ReadOnlySpan<byte> record)
    {
        RequireRecordSize(record, 16, "SCALEKEYS");
        return new PSAScaleKey(ReadVector3(record, 0), ReadSingle(record, 12));
    }

    private static void FixCue4ParseFrameStartIndices(List<PSASequence> sequences)
    {
        if (sequences.Count == 0 || sequences[0].FrameStartIndex != sequences[0].FrameCount)
        {
            return;
        }

        var frameStartIndex = 0;
        for (var index = 0; index < sequences.Count; index++)
        {
            var sequence = sequences[index];
            sequences[index] = sequence with { FrameStartIndex = frameStartIndex };
            frameStartIndex += sequence.FrameCount;
        }
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

        var text = encoding.GetString(value[..length]).TrimEnd();
        return string.IsNullOrWhiteSpace(text) ? "default" : text;
    }

    private static int ReadInt32(ReadOnlySpan<byte> value, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(value.Slice(offset, 4));
    }

    private static float ReadSingle(ReadOnlySpan<byte> value, int offset)
    {
        return BitConverter.Int32BitsToSingle(ReadInt32(value, offset));
    }
}
