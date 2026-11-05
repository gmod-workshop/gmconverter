using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using GMConverter.SDK.Common;

namespace GMConverter.UnrealEngine.Formats.UE3.Packages;

/// <summary>A little-endian reader over decompressed package bytes that also resolves names.</summary>
internal sealed class UE3Archive(byte[] data, IReadOnlyList<string> names, int position = 0)
{
    public byte[] Data { get; } = data;

    public int Position { get; set; } = position;

    public IReadOnlyList<string> Names { get; } = names;

    public byte ReadByte()
    {
        Require(1);
        return Data[Position++];
    }

    public short ReadInt16()
    {
        Require(2);
        var value = BinaryPrimitives.ReadInt16LittleEndian(Data.AsSpan(Position));
        Position += 2;
        return value;
    }

    public ushort ReadUInt16()
    {
        Require(2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(Position));
        Position += 2;
        return value;
    }

    public int ReadInt32()
    {
        Require(4);
        var value = BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan(Position));
        Position += 4;
        return value;
    }

    public uint ReadUInt32()
    {
        Require(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(Position));
        Position += 4;
        return value;
    }

    public ulong ReadUInt64()
    {
        Require(8);
        var value = BinaryPrimitives.ReadUInt64LittleEndian(Data.AsSpan(Position));
        Position += 8;
        return value;
    }

    public float ReadSingle()
    {
        Require(4);
        var value = BinaryPrimitives.ReadSingleLittleEndian(Data.AsSpan(Position));
        Position += 4;
        return value;
    }

    public Half ReadHalf()
    {
        Require(2);
        var value = BinaryPrimitives.ReadHalfLittleEndian(Data.AsSpan(Position));
        Position += 2;
        return value;
    }

    public Vector3 ReadVector3()
    {
        return new Vector3(ReadSingle(), ReadSingle(), ReadSingle());
    }

    public Quaternion ReadQuaternion()
    {
        return new Quaternion(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
    }

    /// <summary>An FName: an index into the name table and an instance number (shown as a _N suffix).</summary>
    public string ReadName()
    {
        var index = ReadInt32();
        var number = ReadInt32();
        if (index < 0 || index >= Names.Count)
        {
            throw new GMConverterException($"UE3 name index {index} is outside the name table at 0x{Position - 8:X}.");
        }

        return number == 0 ? Names[index] : FormattableString.Invariant($"{Names[index]}_{number - 1}");
    }

    /// <summary>An FString: a length (negative for UTF-16) including the terminator, then the characters.</summary>
    public string ReadString()
    {
        var length = ReadInt32();
        if (length == 0)
        {
            return string.Empty;
        }

        if (length < 0)
        {
            var byteCount = checked(-length * 2);
            Require(byteCount);
            var text = Encoding.Unicode.GetString(Data, Position, byteCount);
            Position += byteCount;
            return text.TrimEnd('\0');
        }

        Require(length);
        var value = Encoding.Latin1.GetString(Data, Position, length);
        Position += length;
        return value.TrimEnd('\0');
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        Require(count);
        var span = Data.AsSpan(Position, count);
        Position += count;
        return span;
    }

    public void Skip(int count)
    {
        Require(count);
        Position += count;
    }

    /// <summary>A TArray: an element count, then each element.</summary>
    public List<T> ReadArray<T>(Func<UE3Archive, T> readElement)
    {
        var count = ReadCount();
        List<T> values = new(count);
        for (var index = 0; index < count; index++)
        {
            values.Add(readElement(this));
        }

        return values;
    }

    /// <summary>A bulk-serialized TArray: the element size, then a TArray stored as raw bytes. Returns the element size, count and data offset.</summary>
    public (int ElementSize, int Count, int Offset) ReadBulkArray()
    {
        var elementSize = ReadInt32();
        var count = ReadCount();
        var offset = Position;
        Skip(checked(elementSize * count));
        return (elementSize, count, offset);
    }

    public int ReadCount()
    {
        var count = ReadInt32();
        if (count < 0 || count > Data.Length)
        {
            throw new GMConverterException($"UE3 array count {count} at 0x{Position - 4:X} is out of range.");
        }

        return count;
    }

    private void Require(int count)
    {
        if (count < 0 || Position + count > Data.Length)
        {
            throw new GMConverterException($"UE3 read of {count} bytes at 0x{Position:X} runs past the end of the data.");
        }
    }
}
