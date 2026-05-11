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
    private static readonly byte[] Magic = "EANM"u8.ToArray();
    private const uint HeaderId = 0x00060000;

    public string Path { get; }
    public uint DurationFrames { get; private set; }
    public IReadOnlyList<string> Entities => entities;
    public IReadOnlyList<MOWAnimationFrame> Frames => frames;

    private readonly List<string> entities = [];
    private readonly List<MOWAnimationFrame> frames = [];

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

        var magic = reader.ReadBytes(4);
        if (!magic.SequenceEqual(Magic))
        {
            throw new GMConverterException($"Unsupported Men of War ANM file: {Path}");
        }

        var headerId = reader.ReadUInt32();
        if (headerId != HeaderId)
        {
            throw new GMConverterException($"Unsupported Men of War ANM header 0x{headerId:X8} in {Path}.");
        }

        while (stream.Position < stream.Length)
        {
            var entry = ReadAscii(reader, 4);
            switch (entry)
            {
                case "FRMS":
                    DurationFrames = reader.ReadUInt32();
                    break;

                case "BMAP":
                    ReadEntityMap(reader);
                    break;

                case "FRM2":
                    frames.Add(ReadFrame(reader));
                    break;

                default:
                    throw new GMConverterException($"Unsupported Men of War ANM entry '{entry}' in {Path}.");
            }
        }
    }

    private void ReadEntityMap(BinaryReader reader)
    {
        var count = reader.ReadUInt32();
        for (var index = 0; index < count; index++)
        {
            var nameLength = checked((int)reader.ReadUInt32());
            entities.Add(Encoding.UTF8.GetString(reader.ReadBytes(nameLength)));
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
                SkipVertexAnimation(reader, type.HasFlag(MOWAnimationChunkType.Position));
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

    private static void SkipVertexAnimation(BinaryReader reader, bool hasPosition)
    {
        var byteCount = reader.ReadUInt32();
        reader.BaseStream.Seek(4, SeekOrigin.Current);
        _ = reader.ReadUInt16();
        reader.BaseStream.Seek(2 + byteCount, SeekOrigin.Current);
        reader.BaseStream.Seek(hasPosition ? 32 : 8, SeekOrigin.Current);
    }

    private static string ReadAscii(BinaryReader reader, int count)
    {
        return Encoding.ASCII.GetString(reader.ReadBytes(count));
    }
}
