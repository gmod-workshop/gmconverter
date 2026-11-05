using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace GMConverter.SourceEngine.Particles;

/// <summary>
/// Writes DMX binary encoding 2, the form Source's particle files (<c>format pcf 1</c>) use:
/// a table of type and attribute names (16-bit count and indices), the elements' types, names and
/// ids, then their attributes, with element names and string values inline. The root is element 0;
/// ids are derived from a seed and each element's position, so the same input
/// writes the same bytes.
/// </summary>
internal static class DmxBinaryWriter
{
    public static void Write(Stream stream, DmxElement root, string format, int formatVersion, string seed)
    {
        List<DmxElement> elements = [];
        Dictionary<DmxElement, int> indexes = new(ReferenceEqualityComparer.Instance);
        void Visit(DmxElement element)
        {
            if (!indexes.TryAdd(element, elements.Count))
            {
                return;
            }

            elements.Add(element);
            foreach (var attribute in element.Attributes)
            {
                switch (attribute.Value)
                {
                    case DmxElement child:
                        Visit(child);
                        break;
                    case IReadOnlyList<DmxElement> children:
                        foreach (var child in children)
                        {
                            Visit(child);
                        }

                        break;
                }
            }
        }

        Visit(root);
        List<string> strings = [];
        Dictionary<string, int> stringIndexes = new(StringComparer.Ordinal);
        short Intern(string value)
        {
            if (!stringIndexes.TryGetValue(value, out var index))
            {
                index = strings.Count;
                stringIndexes[value] = index;
                strings.Add(value);
            }

            return checked((short)index);
        }

        foreach (var element in elements)
        {
            Intern(element.Type);
            foreach (var attribute in element.Attributes)
            {
                Intern(attribute.Name);
            }
        }

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteCString(writer, $"<!-- dmx encoding binary 2 format {format} {formatVersion} -->\n");
        writer.Write(checked((ushort)strings.Count));
        foreach (var value in strings)
        {
            WriteCString(writer, value);
        }

        writer.Write(elements.Count);
        for (var index = 0; index < elements.Count; index++)
        {
            writer.Write(Intern(elements[index].Type));
            WriteCString(writer, elements[index].Name);
            writer.Write(ElementId(seed, index).ToByteArray());
        }

        foreach (var element in elements)
        {
            writer.Write(element.Attributes.Count);
            foreach (var attribute in element.Attributes)
            {
                writer.Write(Intern(attribute.Name));
                writer.Write((byte)attribute.Type);
                WriteValue(writer, attribute, indexes);
            }
        }
    }

    private static void WriteValue(BinaryWriter writer, DmxAttribute attribute, Dictionary<DmxElement, int> indexes)
    {
        switch (attribute.Type)
        {
            case DmxAttributeType.Element:
                writer.Write(attribute.Value is DmxElement element ? indexes[element] : -1);
                break;
            case DmxAttributeType.ElementArray:
                var children = (IReadOnlyList<DmxElement>)attribute.Value!;
                writer.Write(children.Count);
                foreach (var child in children)
                {
                    writer.Write(indexes[child]);
                }

                break;
            case DmxAttributeType.Int:
                writer.Write((int)attribute.Value!);
                break;
            case DmxAttributeType.Float:
                writer.Write((float)attribute.Value!);
                break;
            case DmxAttributeType.Bool:
                writer.Write((bool)attribute.Value! ? (byte)1 : (byte)0);
                break;
            case DmxAttributeType.String:
                WriteCString(writer, (string)attribute.Value!);
                break;
            case DmxAttributeType.Color:
                writer.Write((byte[])attribute.Value!);
                break;
            case DmxAttributeType.Vector3:
                var vector = (Vector3)attribute.Value!;
                writer.Write(vector.X);
                writer.Write(vector.Y);
                writer.Write(vector.Z);
                break;
            default:
                throw new InvalidOperationException($"DMX attribute {attribute.Name} has unsupported type {attribute.Type}.");
        }
    }

    private static Guid ElementId(string seed, int index) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{seed}\0{index}"))).AsSpan(0, 16));

    private static void WriteCString(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.UTF8.GetBytes(value));
        writer.Write((byte)0);
    }
}
