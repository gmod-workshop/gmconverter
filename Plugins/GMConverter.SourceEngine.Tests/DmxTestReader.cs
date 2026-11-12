using System.Numerics;
using System.Text;

namespace GMConverter.SourceEngine.Tests;

/// <summary>Reads binary DMX encoding 2 back into elements, so tests can check what Source will load.</summary>
internal static class DmxTestReader
{
    public sealed record Element(string Type, string Name, Dictionary<string, object?> Attributes)
    {
        public T Get<T>(string name) => (T)Attributes[name]!;

        public IReadOnlyList<Element> Children(string name) => (IReadOnlyList<Element>)Attributes[name]!;

        public IEnumerable<string> Functions(string name) => Children(name).Select(child => child.Get<string>("functionName"));

        public Element Function(string list, string function) => Children(list).Single(child => child.Get<string>("functionName") == function);
    }

    public static (string Header, Element Root) Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        var header = CString(reader);
        var strings = Enumerable.Range(0, reader.ReadUInt16()).Select(_ => CString(reader)).ToArray();
        var count = reader.ReadInt32();
        var elements = new Element[count];
        for (var index = 0; index < count; index++)
        {
            var type = strings[reader.ReadInt16()];
            var name = CString(reader);
            reader.ReadBytes(16);
            elements[index] = new Element(type, name, []);
        }

        List<(Element Owner, string Name, object Indexes)> links = [];
        foreach (var element in elements)
        {
            var attributes = reader.ReadInt32();
            for (var attribute = 0; attribute < attributes; attribute++)
            {
                var name = strings[reader.ReadInt16()];
                var type = reader.ReadByte();
                switch (type)
                {
                    case 1:
                        links.Add((element, name, reader.ReadInt32()));
                        break;
                    case 15:
                        links.Add((element, name, Enumerable.Range(0, reader.ReadInt32()).Select(_ => reader.ReadInt32()).ToArray()));
                        break;
                    case 2:
                        element.Attributes[name] = reader.ReadInt32();
                        break;
                    case 3:
                        element.Attributes[name] = reader.ReadSingle();
                        break;
                    case 4:
                        element.Attributes[name] = reader.ReadByte() != 0;
                        break;
                    case 5:
                        element.Attributes[name] = CString(reader);
                        break;
                    case 8:
                        element.Attributes[name] = reader.ReadBytes(4);
                        break;
                    case 10:
                        element.Attributes[name] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        break;
                    default:
                        throw new InvalidDataException($"Unexpected DMX attribute type {type} for {name}.");
                }
            }
        }

        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        foreach (var (owner, name, indexes) in links)
        {
            owner.Attributes[name] = indexes is int single ? (single < 0 ? null : elements[single]) : ((int[])indexes).Select(index => elements[index]).ToList();
        }

        return (header, elements[0]);
    }

    private static string CString(BinaryReader reader)
    {
        List<byte> bytes = [];
        for (var value = reader.ReadByte(); value != 0; value = reader.ReadByte())
        {
            bytes.Add(value);
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }
}
