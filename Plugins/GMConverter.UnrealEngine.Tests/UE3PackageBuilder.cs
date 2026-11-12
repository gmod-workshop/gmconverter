using System.Text;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Tests;

/// <summary>
/// Builds a small uncompressed v872 UE3 package for tests: Engine classes as imports, and exports
/// written as tagged properties (and optional native data after them). Exports are numbered from 1
/// in the order they're added, as package indices.
/// </summary>
internal sealed class UE3PackageBuilder
{
    private readonly List<string> _names = ["None", "Core", "Engine", "Package", "Class"];
    private readonly List<string> _classes = [];
    private readonly List<(string Name, string Class, int Outer, byte[] Body)> _exports = [];

    /// <summary>Adds an export, returning its package index.</summary>
    public int Add(string name, string className, Action<Properties> properties, int outer = 0, Action<BinaryWriter>? native = null)
    {
        if (!_classes.Contains(className))
        {
            _classes.Add(className);
        }

        Name(name);
        Name(className);
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        writer.Write(0); // net index
        properties(new Properties(this, writer));
        writer.Write(Name("None"));
        writer.Write(0);
        native?.Invoke(writer);
        writer.Flush();
        _exports.Add((name, className, outer, body.ToArray()));
        return _exports.Count;
    }

    /// <summary>The package, read back through a game content folder holding only it.</summary>
    public (UE3GameContent Content, UE3Package Package) Read()
    {
        var directory = PskFixtures.CreateDirectory();
        var path = Path.Join(directory, "package");
        File.WriteAllBytes(path, Write());
        var content = UE3GameContent.Open(directory);
        return (content, content.GetPackage(path));
    }

    public int Name(string name)
    {
        var index = _names.IndexOf(name);
        if (index >= 0)
        {
            return index;
        }

        _names.Add(name);
        return _names.Count - 1;
    }

    private byte[] Write()
    {
        const int headerSize = 4 + 4 + 4 + 4 + 5 + 4 + 4 * 7 + 4 * 4 + 16 + 4 + 4 + 4 + 4 + 4;
        const int exportSize = 4 * 3 + 8 + 4 + 8 + 4 + 4 + 4 + 4 + 16 + 4;
        using var tables = new MemoryStream();
        using var writer = new BinaryWriter(tables);

        var nameOffset = headerSize;
        foreach (var name in _names)
        {
            writer.Write(name.Length + 1);
            writer.Write(Encoding.ASCII.GetBytes(name));
            writer.Write((byte)0);
            writer.Write(0x0007001000000000UL);
        }

        // Import 1 is Engine; the classes follow, so class N is import -(N + 2).
        var importOffset = headerSize + (int)tables.Position;
        writer.Write((long)Name("Core"));
        writer.Write((long)Name("Package"));
        writer.Write(0);
        writer.Write((long)Name("Engine"));
        foreach (var className in _classes)
        {
            writer.Write((long)Name("Core"));
            writer.Write((long)Name("Class"));
            writer.Write(-1);
            writer.Write((long)Name(className));
        }

        var exportOffset = headerSize + (int)tables.Position;
        var serialOffset = exportOffset + exportSize * _exports.Count;
        foreach (var (name, className, outer, body) in _exports)
        {
            writer.Write(-(_classes.IndexOf(className) + 2));
            writer.Write(0); // super
            writer.Write(outer);
            writer.Write((long)Name(name));
            writer.Write(0); // archetype
            writer.Write(0UL); // flags
            writer.Write(body.Length);
            writer.Write(serialOffset);
            writer.Write(new byte[exportSize - 4 * 3 - 8 - 4 - 8 - 4 - 4]);
            serialOffset += body.Length;
        }

        foreach (var (_, _, _, body) in _exports)
        {
            writer.Write(body);
        }

        writer.Flush();
        using var file = new MemoryStream();
        using var summary = new BinaryWriter(file);
        summary.Write(UE3Compression.PackageTag);
        summary.Write((ushort)872);
        summary.Write((ushort)5);
        summary.Write(exportOffset + exportSize * _exports.Count);
        summary.Write(5);
        summary.Write(Encoding.ASCII.GetBytes("None\0"));
        summary.Write(0u);
        summary.Write(_names.Count);
        summary.Write(nameOffset);
        summary.Write(_exports.Count);
        summary.Write(exportOffset);
        summary.Write(1 + _classes.Count);
        summary.Write(importOffset);
        summary.Write(exportOffset); // depends
        summary.Write(new byte[4 * 4]);
        summary.Write(new byte[16]);
        summary.Write(0); // generations
        summary.Write(10897);
        summary.Write(0);
        summary.Write(0u);
        summary.Write(0);
        summary.Write(tables.ToArray());
        return file.ToArray();
    }

    /// <summary>Writes an object's tagged properties.</summary>
    internal sealed class Properties(UE3PackageBuilder builder, BinaryWriter writer)
    {
        public void Float(string name, float value)
        {
            Tag(name, "FloatProperty", 4);
            writer.Write(value);
        }

        public void Int(string name, int value)
        {
            Tag(name, "IntProperty", 4);
            writer.Write(value);
        }

        public void Object(string name, int index)
        {
            Tag(name, "ObjectProperty", 4);
            writer.Write(index);
        }

        public void Name(string name, string value)
        {
            Tag(name, "NameProperty", 8);
            WriteName(value);
        }

        public void Enum(string name, string enumType, string value)
        {
            Tag(name, "ByteProperty", 8);
            WriteName(enumType);
            WriteName(value);
        }

        /// <summary>An ExpressionInput reading <paramref name="expression"/>.</summary>
        public void Input(string name, int expression) => Struct(name, "ExpressionInput", inner => inner.Object("Expression", expression));

        public void Struct(string name, string structName, Action<Properties> inner)
        {
            using var body = new MemoryStream();
            using var bodyWriter = new BinaryWriter(body);
            inner(new Properties(builder, bodyWriter));
            bodyWriter.Write((long)builder.Name("None"));
            bodyWriter.Flush();
            Tag(name, "StructProperty", (int)body.Length);
            WriteName(structName);
            writer.Write(body.ToArray());
        }

        public void Objects(string name, params int[] indices)
        {
            Tag(name, "ArrayProperty", 4 + 4 * indices.Length);
            writer.Write(indices.Length);
            foreach (var index in indices)
            {
                writer.Write(index);
            }
        }

        private void Tag(string name, string type, int size)
        {
            WriteName(name);
            WriteName(type);
            writer.Write(size);
            writer.Write(0);
        }

        private void WriteName(string value) => writer.Write((long)builder.Name(value));
    }
}
