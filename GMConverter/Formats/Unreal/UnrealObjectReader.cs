using System.Numerics;
using GMConverter.Common;

namespace GMConverter.Formats.Unreal;

internal sealed class UnrealObjectReader
{
    private const int _maxArrayCount = 10_000_000;

    private readonly UnrealPackageFile _package;
    private readonly BinaryReader _reader;
    private readonly long _stopPosition;

    public UnrealObjectReader(UnrealPackageFile package, BinaryReader reader, UnrealPackageExport export)
    {
        _package = package;
        _reader = reader;
        _reader.BaseStream.Seek(export.SerialOffset, SeekOrigin.Begin);
        _stopPosition = export.SerialOffset + export.SerialSize;
    }

    public int FileVersion => _package.Summary.FileVersion;

    public int LicenseeVersion => _package.Summary.LicenseeVersion;

    public long Position => _reader.BaseStream.Position;

    public long Remaining => _stopPosition - _reader.BaseStream.Position;

    public void SkipProperties()
    {
        _ = ReadProperties();
    }

    public UnrealPropertyCollection ReadProperties()
    {
        var properties = new UnrealPropertyCollection();
        ReadProperties(properties, string.Empty);
        return properties;
    }

    private void ReadProperties(UnrealPropertyCollection properties, string prefix)
    {
        while (Position < _stopPosition)
        {
            var name = ReadName();
            if (name.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var info = ReadByte();
            var propertyType = info & 0x0F;
            var hasArrayIndex = (info & 0x80) != 0;
            var propertyName = prefix + name;

            if (propertyType == 10)
            {
                _ = ReadName();
            }

            var size = ((info >> 4) & 7) switch
            {
                0 => 1,
                1 => 2,
                2 => 4,
                3 => 12,
                4 => 16,
                5 => ReadByte(),
                6 => ReadUInt16(),
                7 => ReadInt32(),
                _ => throw new GMConverterException("Invalid UE2 property size.")
            };

            if (propertyType != 3 && hasArrayIndex)
            {
                SkipArrayIndex();
            }

            if (propertyType == 3)
            {
                continue;
            }

            var valuePosition = Position;
            switch (propertyType)
            {
                case 1:
                    ReadByteProperty(properties, propertyName, size);
                    break;
                case 2:
                    ReadIntProperty(properties, propertyName, size);
                    break;
                case 5:
                case 8:
                    ReadObjectProperty(properties, propertyName, size);
                    break;
                case 6:
                    ReadNameProperty(properties, propertyName, size);
                    break;
                case 9:
                    ReadArrayProperty(properties, propertyName, size);
                    break;
                default:
                    Skip(size);
                    break;
            }

            var consumed = Position - valuePosition;
            if (consumed < size)
            {
                Skip((int)(size - consumed));
            }
        }

        throw new GMConverterException("UE2 property stream did not contain a None terminator.");
    }

    public byte ReadByte()
    {
        EnsureReadable(1);
        return _reader.ReadByte();
    }

    public ushort ReadUInt16()
    {
        EnsureReadable(sizeof(ushort));
        return _reader.ReadUInt16();
    }

    public short ReadInt16()
    {
        EnsureReadable(sizeof(short));
        return _reader.ReadInt16();
    }

    public int ReadInt32()
    {
        EnsureReadable(sizeof(int));
        return _reader.ReadInt32();
    }

    public uint ReadUInt32()
    {
        EnsureReadable(sizeof(uint));
        return _reader.ReadUInt32();
    }

    public float ReadSingle()
    {
        EnsureReadable(sizeof(float));
        return _reader.ReadSingle();
    }

    public string ReadName()
    {
        var nameIndex = ReadCompactIndex();
        if ((uint)nameIndex >= _package.Names.Count)
        {
            throw new GMConverterException($"UE2 object name index is invalid: {nameIndex}");
        }

        return _package.Names[nameIndex];
    }

    public int ReadObjectIndex()
    {
        return ReadCompactIndex();
    }

    public int ReadCompactIndex()
    {
        var firstByte = ReadByte();
        var negative = (firstByte & 0x80) != 0;
        var value = firstByte & 0x3F;
        if ((firstByte & 0x40) != 0)
        {
            var shift = 6;

            for (var i = 0; i < 4; i++)
            {
                firstByte = ReadByte();
                value |= (firstByte & 0x7F) << shift;
                if ((firstByte & 0x80) == 0)
                {
                    break;
                }

                shift += 7;
            }
        }

        return negative ? -value : value;
    }

    public Vector2 ReadVector2()
    {
        return new Vector2(ReadSingle(), ReadSingle());
    }

    public Vector3 ReadVector3()
    {
        return new Vector3(ReadSingle(), ReadSingle(), ReadSingle());
    }

    public Quaternion ReadQuaternion()
    {
        return new Quaternion(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
    }

    public IReadOnlyList<T> ReadArray<T>(Func<T> readItem)
    {
        var count = ReadCompactIndex();
        if (count is < 0 or > _maxArrayCount)
        {
            throw new GMConverterException($"UE2 array count is invalid: {count}");
        }

        var items = new List<T>(count);
        for (var i = 0; i < count; i++)
        {
            items.Add(readItem());
        }

        return items;
    }

    public IReadOnlyList<T> ReadLazyArray<T>(Func<T> readItem)
    {
        if (FileVersion > 61)
        {
            _ = ReadInt32();
        }

        return ReadArray(readItem);
    }

    public IReadOnlyList<int> ReadUInt16Array()
    {
        var count = ReadCompactIndex();
        if (count is < 0 or > _maxArrayCount)
        {
            throw new GMConverterException($"UE2 index array count is invalid: {count}");
        }

        var items = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            items.Add(ReadUInt16());
        }

        return items;
    }

    public void ReadBox()
    {
        _ = ReadVector3();
        _ = ReadVector3();
        _ = ReadByte();
    }

    public void ReadSphere()
    {
        _ = ReadVector3();
        if (FileVersion >= 61)
        {
            _ = ReadSingle();
        }
    }

    public void Skip(int byteCount)
    {
        if (byteCount < 0)
        {
            throw new GMConverterException("Cannot skip a negative UE2 byte count.");
        }

        EnsureReadable(byteCount);
        _reader.BaseStream.Seek(byteCount, SeekOrigin.Current);
    }

    public void SkipRemaining()
    {
        _reader.BaseStream.Seek(_stopPosition, SeekOrigin.Begin);
    }

    private void SkipArrayIndex()
    {
        var value = ReadByte();
        if (value < 128)
        {
            return;
        }

        _ = ReadByte();
        if ((value & 0x40) != 0)
        {
            _ = ReadByte();
            _ = ReadByte();
        }
    }

    private void ReadByteProperty(UnrealPropertyCollection properties, string propertyName, int size)
    {
        if (size == 1)
        {
            properties.AddInteger(propertyName, ReadByte());
            return;
        }

        Skip(size);
    }

    private void ReadIntProperty(UnrealPropertyCollection properties, string propertyName, int size)
    {
        if (size == sizeof(int))
        {
            properties.AddInteger(propertyName, ReadInt32());
            return;
        }

        Skip(size);
    }

    private void ReadObjectProperty(UnrealPropertyCollection properties, string propertyName, int size)
    {
        var startPosition = Position;
        var objectIndex = ReadObjectIndex();
        properties.AddObjectReference(propertyName, objectIndex);

        var consumed = Position - startPosition;
        if (consumed > size)
        {
            throw new GMConverterException($"UE2 object property size is invalid for {propertyName}.");
        }
    }

    private void ReadNameProperty(UnrealPropertyCollection properties, string propertyName, int size)
    {
        var startPosition = Position;
        var name = ReadName();
        properties.AddName(propertyName, name);

        var consumed = Position - startPosition;
        if (consumed > size)
        {
            throw new GMConverterException($"UE2 name property size is invalid for {propertyName}.");
        }
    }

    private void ReadArrayProperty(UnrealPropertyCollection properties, string propertyName, int size)
    {
        var valuePosition = Position;
        var endPosition = valuePosition + size;
        var count = ReadCompactIndex();
        if (count is < 0 or > _maxArrayCount)
        {
            throw new GMConverterException($"UE2 array property count is invalid for {propertyName}: {count}");
        }

        if (propertyName.Equals("Materials", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                for (var i = 0; i < count && Position < endPosition; i++)
                {
                    ReadProperties(properties, propertyName + ".");
                }
            }
            catch (GMConverterException)
            {
                _reader.BaseStream.Seek(endPosition, SeekOrigin.Begin);
            }
        }

        _reader.BaseStream.Seek(endPosition, SeekOrigin.Begin);
    }

    private void EnsureReadable(int byteCount)
    {
        if (byteCount < 0 || Position + byteCount > _stopPosition)
        {
            throw new GMConverterException("Unexpected end of UE2 object data.");
        }
    }
}
