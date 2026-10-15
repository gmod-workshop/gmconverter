using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using GMConverter.SDK.Common;

namespace GMConverter.Godot.Resources;

/// <summary>
/// A Godot binary resource (<c>.res</c>, <c>.scn</c> and the files the importer writes, such as
/// <c>.oggvorbisstr</c>): a main resource and the resources built into the file with it, which
/// refer to each other and to resources in other files.
/// </summary>
internal sealed class GodotResourceFile
{
    private const int _reservedFieldCount = 11;
    private const uint _namedSceneIdsFlag = 1;
    private const uint _uidsFlag = 2;
    private const uint _scriptClassFlag = 8;
    private const int _firstFormatWithoutNodePathProperty = 3;

    private GodotResourceFile(string type, IReadOnlyList<GodotExternalResource> externalResources, IReadOnlyList<GodotResource> resources)
    {
        Type = type;
        ExternalResources = externalResources;
        Resources = resources;
    }

    /// <summary>The main resource's class, such as <c>PackedScene</c>.</summary>
    public string Type { get; }

    public IReadOnlyList<GodotExternalResource> ExternalResources { get; }

    /// <summary>The resources in the file, each after the ones it refers to; the main resource is last.</summary>
    public IReadOnlyList<GodotResource> Resources { get; }

    public GodotResource Main => Resources[^1];

    /// <summary>Whether <paramref name="data"/> starts like a binary resource, compressed or not.</summary>
    public static bool IsResource(ReadOnlySpan<byte> data)
    {
        return data.Length >= 4 && (data[..4].SequenceEqual("RSRC"u8) || data[..4].SequenceEqual("RSCC"u8));
    }

    public static GodotResourceFile Read(byte[] data)
    {
        if (!IsResource(data))
        {
            throw new GMConverterException("Not a Godot binary resource.");
        }

        // A compressed file's contents continue from the magic, so the header follows directly.
        return data.AsSpan(0, 4).SequenceEqual("RSCC"u8)
            ? new Reader(GodotCompressedFile.Decompress(data), 0).Read()
            : new Reader(data, 4).Read();
    }

    private sealed class Reader(byte[] data, int start)
    {
        private readonly List<string> _strings = [];
        private readonly List<GodotExternalResource> _external = [];
        private readonly Dictionary<string, GodotResource> _resourcesById = new(StringComparer.Ordinal);
        private readonly List<(string Id, long Offset)> _internal = [];
        private int _position = start;
        private bool _bigEndian;
        private bool _realIsDouble;
        private bool _namedSceneIds;
        private int _format;

        public GodotResourceFile Read()
        {
            _bigEndian = ReadUInt32() != 0;
            _realIsDouble = ReadUInt32() != 0;
            ReadUInt32(); // major
            ReadUInt32(); // minor
            _format = (int)ReadUInt32();
            var type = ReadString();
            ReadUInt64(); // import metadata offset
            var flags = ReadUInt32();
            _namedSceneIds = (flags & _namedSceneIdsFlag) != 0;
            ReadUInt64(); // uid
            if ((flags & _scriptClassFlag) != 0)
            {
                ReadString();
            }

            _position += _reservedFieldCount * 4;

            var stringCount = ReadUInt32();
            for (var i = 0; i < stringCount; i++)
            {
                _strings.Add(ReadString());
            }

            var externalCount = ReadUInt32();
            for (var i = 0; i < externalCount; i++)
            {
                var resourceType = ReadString();
                var path = ReadString();
                var uid = (flags & _uidsFlag) != 0 ? (long)ReadUInt64() : -1;
                _external.Add(new GodotExternalResource(resourceType, path, uid));
            }

            var internalCount = ReadUInt32();
            for (var i = 0; i < internalCount; i++)
            {
                var path = ReadString();
                var offset = (long)ReadUInt64();
                _internal.Add((path.StartsWith("local://", StringComparison.Ordinal) ? path["local://".Length..] : path, offset));
            }

            List<GodotResource> resources = [];
            foreach (var (id, offset) in _internal)
            {
                _position = checked((int)offset);
                var resourceType = ReadString();
                var count = ReadUInt32();
                Dictionary<string, object?> properties = new(StringComparer.Ordinal);
                for (var i = 0; i < count; i++)
                {
                    var name = ReadName();
                    properties[name] = ReadVariant();
                }

                var resource = new GodotResource(resourceType, id, properties);
                _resourcesById[id] = resource;
                resources.Add(resource);
            }

            return new GodotResourceFile(type, _external, resources);
        }

        private object? ReadVariant()
        {
            var type = ReadUInt32();
            switch (type)
            {
                case 1: return null;
                case 2: return ReadUInt32() != 0;
                case 3: return (long)(int)ReadUInt32();
                case 4: return (double)ReadReal();
                case 5: return ReadString();
                case 10: return new Vector2(ReadReal(), ReadReal());
                case 11: return new Vector4(ReadReal(), ReadReal(), ReadReal(), ReadReal()); // Rect2: position, size
                case 12: return ReadVector3();
                case 13: return new Plane(ReadVector3(), ReadReal());
                case 14: return new Quaternion(ReadReal(), ReadReal(), ReadReal(), ReadReal());
                case 15: return new GodotAabb(ReadVector3(), ReadVector3());
                case 16: return new GodotBasis(ReadVector3(), ReadVector3(), ReadVector3());
                case 17: return new GodotTransform3D(ReadVector3(), ReadVector3(), ReadVector3(), ReadVector3());
                case 18: return ReadReals(6); // Transform2D
                case 20: return new GodotColor(ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());
                case 22: return ReadNodePath();
                case 23: return (long)ReadUInt32(); // RID
                case 24: return ReadObject();
                case 26: return ReadDictionary();
                case 30: return ReadArray();
                case 31: return ReadBytes();
                case 32: return ReadInt32s();
                case 33: return ReadFloats(checked((int)ReadUInt32()));
                case 34: return ReadStrings();
                case 35: return ReadVectors(3, values => new Vector3(values[0], values[1], values[2]));
                case 36: return ReadColors();
                case 37: return ReadVectors(2, values => new Vector2(values[0], values[1]));
                case 40: return (long)ReadUInt64();
                case 41: return ReadDouble();
                case 42 or 43: return null; // Callable, Signal: saved empty
                case 44: return ReadString(); // StringName
                case 45: return new[] { (int)ReadUInt32(), (int)ReadUInt32() };
                case 46: return new[] { (int)ReadUInt32(), (int)ReadUInt32(), (int)ReadUInt32(), (int)ReadUInt32() };
                case 47: return new[] { (int)ReadUInt32(), (int)ReadUInt32(), (int)ReadUInt32() };
                case 48: return ReadInt64s();
                case 49: return ReadDoubles();
                case 50: return new Vector4(ReadReal(), ReadReal(), ReadReal(), ReadReal());
                case 51: return new[] { (int)ReadUInt32(), (int)ReadUInt32(), (int)ReadUInt32(), (int)ReadUInt32() };
                case 52: return ReadReals(16); // Projection
                case 53: return ReadVectors(4, values => new Vector4(values[0], values[1], values[2], values[3]));
                default:
                    throw new GMConverterException(string.Create(CultureInfo.InvariantCulture, $"Godot resource holds a value of unknown type {type} at byte {_position - 4}."));
            }
        }

        private object? ReadObject()
        {
            var kind = ReadUInt32();
            switch (kind)
            {
                case 0:
                    return null;
                case 1:
                    return new GodotExternalResource(ReadString(), ReadString());
                case 2:
                    {
                        // Named ids refer to a resource by its place in the file; numbered ids by the number.
                        var index = (int)ReadUInt32();
                        var id = _namedSceneIds
                            ? index < _internal.Count ? _internal[index].Id : null
                            : index.ToString(CultureInfo.InvariantCulture);
                        return id is not null && _resourcesById.TryGetValue(id, out var resource) ? resource : null;
                    }

                case 3:
                    {
                        var index = (int)ReadUInt32();
                        return index < _external.Count ? _external[index] : null;
                    }

                default:
                    throw new GMConverterException($"Godot resource holds an object reference of unknown kind {kind}.");
            }
        }

        private GodotNodePath ReadNodePath()
        {
            var nameCount = ReadUInt16();
            var subNameCount = ReadUInt16();
            var absolute = (subNameCount & 0x8000) != 0;
            subNameCount &= 0x7FFF;
            var names = Enumerable.Range(0, nameCount).Select(_ => ReadName()).ToList();
            var subNames = Enumerable.Range(0, subNameCount).Select(_ => ReadName()).ToList();
            if (_format < _firstFormatWithoutNodePathProperty && ReadName() is { Length: > 0 } property)
            {
                subNames.Add(property);
            }

            return new GodotNodePath(names, subNames, absolute);
        }

        private Dictionary<object, object?> ReadDictionary()
        {
            var count = ReadUInt32() & 0x7FFFFFFF;
            Dictionary<object, object?> dictionary = [];
            for (var i = 0; i < count; i++)
            {
                var key = ReadVariant() ?? string.Empty;
                dictionary[key] = ReadVariant();
            }

            return dictionary;
        }

        private List<object?> ReadArray()
        {
            var count = ReadUInt32() & 0x7FFFFFFF;
            List<object?> list = new((int)count);
            for (var i = 0; i < count; i++)
            {
                list.Add(ReadVariant());
            }

            return list;
        }

        private byte[] ReadBytes()
        {
            var count = checked((int)ReadUInt32());
            var bytes = data.AsSpan(_position, count).ToArray();
            _position += (count + 3) & ~3;
            return bytes;
        }

        private int[] ReadInt32s()
        {
            var values = new int[ReadUInt32()];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = (int)ReadUInt32();
            }

            return values;
        }

        private long[] ReadInt64s()
        {
            var values = new long[ReadUInt32()];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = (long)ReadUInt64();
            }

            return values;
        }

        private float[] ReadFloats(int count)
        {
            var values = new float[count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = ReadFloat();
            }

            return values;
        }

        private double[] ReadDoubles()
        {
            var values = new double[ReadUInt32()];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = ReadDouble();
            }

            return values;
        }

        private float[] ReadReals(int count)
        {
            var values = new float[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = ReadReal();
            }

            return values;
        }

        private string[] ReadStrings()
        {
            var values = new string[ReadUInt32()];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = ReadString();
            }

            return values;
        }

        private T[] ReadVectors<T>(int components, Func<float[], T> create)
        {
            var values = new T[ReadUInt32()];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = create(ReadReals(components));
            }

            return values;
        }

        private GodotColor[] ReadColors()
        {
            var values = new GodotColor[ReadUInt32()];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = new GodotColor(ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());
            }

            return values;
        }

        private Vector3 ReadVector3()
        {
            return new Vector3(ReadReal(), ReadReal(), ReadReal());
        }

        // A property name: an index into the string table, or, with the top bit set, the string itself.
        private string ReadName()
        {
            var id = ReadUInt32();
            if ((id & 0x80000000) == 0)
            {
                return _strings[(int)id];
            }

            var length = (int)(id & 0x7FFFFFFF);
            var value = Encoding.UTF8.GetString(data, _position, length).TrimEnd('\0');
            _position += length;
            return value;
        }

        private string ReadString()
        {
            var length = checked((int)ReadUInt32());
            var value = Encoding.UTF8.GetString(data, _position, length).TrimEnd('\0');
            _position += length;
            return value;
        }

        private float ReadReal()
        {
            return _realIsDouble ? (float)ReadDouble() : ReadFloat();
        }

        private float ReadFloat()
        {
            return BitConverter.Int32BitsToSingle((int)ReadUInt32());
        }

        private double ReadDouble()
        {
            return BitConverter.Int64BitsToDouble((long)ReadUInt64());
        }

        private ushort ReadUInt16()
        {
            var span = data.AsSpan(_position, 2);
            _position += 2;
            return _bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(span) : BinaryPrimitives.ReadUInt16LittleEndian(span);
        }

        private uint ReadUInt32()
        {
            var span = data.AsSpan(_position, 4);
            _position += 4;
            return _bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span);
        }

        private ulong ReadUInt64()
        {
            var span = data.AsSpan(_position, 8);
            _position += 8;
            return _bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(span) : BinaryPrimitives.ReadUInt64LittleEndian(span);
        }
    }
}
