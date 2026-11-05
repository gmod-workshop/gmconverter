using System.Buffers.Binary;
using System.Numerics;
using GMConverter.SDK.Common;

namespace GMConverter.UnrealEngine.Formats.UE3.Packages;

/// <summary>
/// An object's tagged properties, read up to the terminating <c>None</c>. Values stay in the
/// package data; the accessors decode the common types.
/// </summary>
internal sealed class UE3Properties
{
    private const int _maximumProperties = 100_000;

    private readonly byte[] _data;
    private readonly IReadOnlyList<string> _names;
    private readonly List<UE3Property> _properties;

    private UE3Properties(byte[] data, IReadOnlyList<string> names, List<UE3Property> properties)
    {
        _data = data;
        _names = names;
        _properties = properties;
    }

    public IReadOnlyList<UE3Property> All => _properties;

    /// <summary>Reads properties from the archive's position, leaving it just past the terminating <c>None</c>.</summary>
    public static UE3Properties Read(UE3Archive archive, int fileVersion)
    {
        List<UE3Property> properties = [];
        while (properties.Count < _maximumProperties)
        {
            var name = archive.ReadName();
            if (name.Equals("None", StringComparison.Ordinal))
            {
                return new UE3Properties(archive.Data, archive.Names, properties);
            }

            var type = archive.ReadName();
            var size = archive.ReadInt32();
            var arrayIndex = archive.ReadInt32();
            switch (type)
            {
                case "BoolProperty":
                    var value = fileVersion >= 673 ? archive.ReadByte() != 0 : archive.ReadInt32() != 0;
                    properties.Add(new UE3Property(name, type, arrayIndex, archive.Position, 0, BoolValue: value));
                    continue;
                case "StructProperty":
                    var structName = archive.ReadName();
                    properties.Add(new UE3Property(name, type, arrayIndex, archive.Position, size, StructName: structName));
                    break;
                case "ByteProperty" when fileVersion >= 633:
                    archive.ReadName(); // enum type
                    var enumValue = size == 8 ? new UE3Archive(archive.Data, archive.Names, archive.Position).ReadName() : null;
                    properties.Add(new UE3Property(name, type, arrayIndex, archive.Position, size, EnumValue: enumValue));
                    break;
                default:
                    properties.Add(new UE3Property(name, type, arrayIndex, archive.Position, size));
                    break;
            }

            archive.Skip(size);
        }

        throw new GMConverterException("UE3 object has too many properties; its data is probably not property-tagged.");
    }

    public UE3Property? Find(string name, int arrayIndex = 0)
    {
        return _properties.FirstOrDefault(property =>
            property.ArrayIndex == arrayIndex && property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public int? GetInt(string name) => Find(name) is { Size: 4 } property ? ReadInt(property.ValueOffset) : null;

    public float? GetFloat(string name) => Find(name) is { Size: 4 } property
        ? BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(property.ValueOffset))
        : null;

    public bool? GetBool(string name) => Find(name)?.BoolValue;

    /// <summary>A name property's value, or the name of a byte property's enum value.</summary>
    public string? GetName(string name)
    {
        return Find(name) switch
        {
            { EnumValue: { } enumValue } => enumValue,
            { Type: "NameProperty", Size: 8 } property => Open(property.ValueOffset).ReadName(),
            _ => null,
        };
    }

    public string? GetString(string name) => Find(name) is { Type: "StrProperty" } property ? Open(property.ValueOffset).ReadString() : null;

    /// <summary>A plain byte property's value (not an enum name); null when absent.</summary>
    public byte? GetByte(string name) => Find(name) is { Type: "ByteProperty", Size: 1 } property ? _data[property.ValueOffset] : null;

    /// <summary>An object property's package index; zero for None or a missing property.</summary>
    public int GetObject(string name) => Find(name) is { Size: 4 } property ? ReadInt(property.ValueOffset) : 0;

    public Vector3? GetVector(string name) => Find(name) is { Type: "StructProperty", Size: 12 } property
        ? Open(property.ValueOffset).ReadVector3()
        : null;

    public Vector2? GetVector2(string name) => Find(name) is { Type: "StructProperty", Size: 8 } property
        ? new Vector2(BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(property.ValueOffset)), BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(property.ValueOffset + 4)))
        : null;

    /// <summary>A rotator as Unreal's 16-bit angle units (65536 per turn): pitch, yaw, roll.</summary>
    public (int Pitch, int Yaw, int Roll)? GetRotator(string name)
    {
        if (Find(name) is not { Type: "StructProperty", Size: 12 } property)
        {
            return null;
        }

        var archive = Open(property.ValueOffset);
        return (archive.ReadInt32(), archive.ReadInt32(), archive.ReadInt32());
    }

    /// <summary>A reader positioned at an array property's first element, with the element count; null when absent.</summary>
    public (UE3Archive Elements, int Count)? GetArray(string name)
    {
        if (Find(name) is not { Type: "ArrayProperty" } property || property.Size < 4)
        {
            return null;
        }

        var archive = Open(property.ValueOffset);
        var count = archive.ReadCount();
        return (archive, count);
    }

    public IReadOnlyList<string> GetNameArray(string name)
    {
        return GetArray(name) is { } array ? [.. Enumerable.Range(0, array.Count).Select(_ => array.Elements.ReadName())] : [];
    }

    public IReadOnlyList<int> GetObjectArray(string name)
    {
        return GetArray(name) is { } array ? [.. Enumerable.Range(0, array.Count).Select(_ => array.Elements.ReadInt32())] : [];
    }

    public IReadOnlyList<int> GetIntArray(string name) => GetObjectArray(name);

    /// <summary>A property-tagged struct's own properties, such as a particle module's distribution; null when absent.</summary>
    public UE3Properties? GetStruct(string name, int fileVersion) =>
        Find(name) is { Type: "StructProperty" } property ? Read(Open(property.ValueOffset), fileVersion) : null;

    /// <summary>Each element of an array of property-tagged structs, such as material parameter values.</summary>
    public IReadOnlyList<UE3Properties> GetStructArray(string name, int fileVersion)
    {
        if (GetArray(name) is not { } array)
        {
            return [];
        }

        List<UE3Properties> elements = new(array.Count);
        for (var index = 0; index < array.Count; index++)
        {
            elements.Add(Read(array.Elements, fileVersion));
        }

        return elements;
    }

    private int ReadInt(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(offset));

    private UE3Archive Open(int offset) => new(_data, _names, offset);
}
