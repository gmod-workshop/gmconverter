using System.Numerics;
using System.Text;
using GMConverter.Common;

namespace GMConverter.Formats.MOW;

[Flags]
internal enum MOWAnimationChunkType : ushort
{
    Position = 1,
    Quaternion = 2,
    InvertedQuaternion = 4,
    Unknown = 8,
    Unknown2 = 16,
    Vertices = 32
}

internal sealed record MOWAnimationEvent(
    int EntityIndex,
    MOWAnimationChunkType Type,
    Vector3? Position,
    Quaternion? Rotation);

internal sealed record MOWAnimationFrame(
    ushort Time,
    IReadOnlyList<MOWAnimationEvent> Events);

internal sealed class MOWAnimationFile
{
    private static readonly byte[] _magic = "EANM"u8.ToArray();
    private const uint _headerId = 0x00060000;

    public string Path { get; }
    public uint DurationFrames { get; private set; }
    public IReadOnlyList<string> Entities => _entities;
    public IReadOnlyList<MOWAnimationFrame> Frames => _frames;

    private readonly List<string> _entities = [];
    private readonly List<MOWAnimationFrame> _frames = [];

    private MOWAnimationFile(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
    }

    public static MOWAnimationFile Read(string path)
    {
        var file = new MOWAnimationFile(path);
        file.Read();
        return file;
    }

    private void Read()
    {
        using var stream = File.OpenRead(Path);
        using var reader = new BinaryReader(stream);

        var magic = ReadBytes(reader, 4, "ANM header");
        if (!magic.SequenceEqual(_magic))
        {
            throw new GMConverterException($"Unsupported Men of War ANM file: {Path}");
        }

        var headerId = reader.ReadUInt32();
        if (headerId != _headerId)
        {
            throw new GMConverterException($"Unsupported Men of War ANM header 0x{headerId:X8} in {Path}.");
        }

        while (stream.Position < stream.Length)
        {
            var entryOffset = stream.Position;
            var entry = ReadAscii(reader, 4, "ANM entry id");
            switch (entry)
            {
                case "FRMS":
                    DurationFrames = reader.ReadUInt32();
                    break;

                case "BMAP":
                    ReadEntityMap(reader);
                    break;

                case "FRM2":
                    _frames.Add(ReadFrame(reader));
                    break;

                default:
                    throw new GMConverterException(
                        $"Unsupported Men of War ANM entry '{entry}' at 0x{entryOffset:X} in {Path}. " +
                        "Supported entries are FRMS, BMAP, and FRM2.");
            }
        }
    }

    private void ReadEntityMap(BinaryReader reader)
    {
        var count = reader.ReadUInt32();
        for (var index = 0; index < count; index++)
        {
            var nameLength = checked((int)reader.ReadUInt32());
            _entities.Add(Encoding.UTF8.GetString(ReadBytes(reader, nameLength, "ANM entity name")));
        }
    }

    private static MOWAnimationFrame ReadFrame(BinaryReader reader)
    {
        var time = reader.ReadUInt16();
        var chunkCount = reader.ReadByte();
        List<MOWAnimationEvent> events = [];

        for (var index = 0; index < chunkCount; index++)
        {
            var entityIndex = reader.ReadByte();
            var type = (MOWAnimationChunkType)reader.ReadUInt16();
            Vector3? position = null;
            Quaternion? rotation = null;

            if (type.HasFlag(MOWAnimationChunkType.Position))
            {
                position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            if (type.HasFlag(MOWAnimationChunkType.Quaternion))
            {
                rotation = ReadQuaternion(reader, type.HasFlag(MOWAnimationChunkType.InvertedQuaternion));
            }

            if (type.HasFlag(MOWAnimationChunkType.Vertices))
            {
                SkipVertexAnimation(reader);
            }

            events.Add(new MOWAnimationEvent(entityIndex, type, position, rotation));
        }

        return new MOWAnimationFrame(time, events);
    }

    private static Quaternion ReadQuaternion(BinaryReader reader, bool inverted)
    {
        var x = reader.ReadSingle();
        var y = reader.ReadSingle();
        var z = reader.ReadSingle();
        var wSquared = 1.0f - (x * x + y * y + z * z);
        var w = wSquared > 0.0f ? MathF.Sqrt(wSquared) : 0.0f;

        return inverted
            ? new Quaternion(y, -x, w, -z)
            : new Quaternion(x, y, z, w);
    }

    private static void SkipVertexAnimation(BinaryReader reader)
    {
        var byteCount = reader.ReadUInt32();
        reader.BaseStream.Seek(4, SeekOrigin.Current);
        var vertexCount = reader.ReadUInt16();
        reader.BaseStream.Seek(2, SeekOrigin.Current);
        var vertexDataLength = checked(vertexCount * 32);
        var trailerLength = checked((long)byteCount - 8 - vertexDataLength);
        if (trailerLength < 0)
        {
            throw new GMConverterException(
                $"Invalid Men of War vertex animation chunk: {byteCount} bytes cannot contain {vertexCount} vertices.");
        }

        reader.BaseStream.Seek(vertexDataLength + trailerLength, SeekOrigin.Current);
    }

    private static string ReadAscii(BinaryReader reader, int count, string description)
    {
        return Encoding.ASCII.GetString(ReadBytes(reader, count, description));
    }

    private static byte[] ReadBytes(BinaryReader reader, int count, string description)
    {
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
        {
            throw new GMConverterException($"Unexpected end of Men of War {description}.");
        }

        return bytes;
    }
}
