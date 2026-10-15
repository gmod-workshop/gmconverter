using System.Numerics;
using System.Text;

namespace GMConverter.Godot.Tests;

/// <summary>
/// Writes small Godot 4 binary resources (format 5, named ids, no uids) for tests. Values are
/// written with the type tags of Godot's <c>ResourceFormatSaverBinary</c>.
/// </summary>
internal sealed class GodotResourceBuilder
{
    private readonly List<(string Type, string Path)> _external = [];
    private readonly List<(string Id, string Type, (string Name, Action<BinaryWriter> Write)[] Properties)> _resources = [];
    private readonly List<string> _strings = [];

    public int AddExternal(string type, string path)
    {
        _external.Add((type, path));
        return _external.Count - 1;
    }

    /// <summary>Adds a resource; returns its index, which <see cref="Internal"/> refers to it by.</summary>
    public int Add(string id, string type, params (string Name, Action<BinaryWriter> Write)[] properties)
    {
        foreach (var (name, _) in properties)
        {
            if (!_strings.Contains(name))
            {
                _strings.Add(name);
            }
        }

        _resources.Add((id, type, properties));
        return _resources.Count - 1;
    }

    /// <summary>
    /// The file. With <paramref name="withoutMagic"/> it is the stream Godot compresses: everything
    /// after the magic, with offsets counted from there.
    /// </summary>
    public byte[] Build(string mainType, bool withoutMagic = false)
    {
        using var body = new MemoryStream();
        var offsets = new List<long>();

        // The header's length depends only on the tables, so lay the resources out after it.
        var header = WriteHeader(mainType, [.. _resources.Select(_ => 0L)], withoutMagic);
        foreach (var (_, type, properties) in _resources)
        {
            offsets.Add(header.Length + body.Length);
            using var writer = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true);
            WriteString(writer, type);
            writer.Write(properties.Length);
            foreach (var (name, write) in properties)
            {
                writer.Write(_strings.IndexOf(name));
                write(writer);
            }
        }

        return [.. WriteHeader(mainType, offsets, withoutMagic), .. body.ToArray()];
    }

    public static Action<BinaryWriter> Int(int value) => writer =>
    {
        writer.Write(3);
        writer.Write(value);
    };

    public static Action<BinaryWriter> Bool(bool value) => writer =>
    {
        writer.Write(2);
        writer.Write(value ? 1 : 0);
    };

    public static Action<BinaryWriter> Float(float value) => writer =>
    {
        writer.Write(4);
        writer.Write(value);
    };

    public static Action<BinaryWriter> Text(string value) => writer =>
    {
        writer.Write(5);
        WriteString(writer, value);
    };

    public static Action<BinaryWriter> Vector(Vector3 value) => writer =>
    {
        writer.Write(12);
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    };

    /// <summary>A Transform3D: the basis rows, then the origin.</summary>
    public static Action<BinaryWriter> Transform(Vector3 row0, Vector3 row1, Vector3 row2, Vector3 origin) => writer =>
    {
        writer.Write(17);
        foreach (var vector in new[] { row0, row1, row2, origin })
        {
            writer.Write(vector.X);
            writer.Write(vector.Y);
            writer.Write(vector.Z);
        }
    };

    public static Action<BinaryWriter> Bytes(byte[] value) => writer =>
    {
        writer.Write(31);
        writer.Write(value.Length);
        writer.Write(value);
        writer.Write(new byte[((value.Length + 3) & ~3) - value.Length]);
    };

    public static Action<BinaryWriter> Int64s(params long[] values) => writer =>
    {
        writer.Write(48);
        writer.Write(values.Length);
        foreach (var value in values)
        {
            writer.Write(value);
        }
    };

    public static Action<BinaryWriter> Int32s(params int[] values) => writer =>
    {
        writer.Write(32);
        writer.Write(values.Length);
        foreach (var value in values)
        {
            writer.Write(value);
        }
    };

    public static Action<BinaryWriter> Float32s(params float[] values) => writer =>
    {
        writer.Write(33);
        writer.Write(values.Length);
        foreach (var value in values)
        {
            writer.Write(value);
        }
    };

    public static Action<BinaryWriter> Strings(params string[] values) => writer =>
    {
        writer.Write(34);
        writer.Write(values.Length);
        foreach (var value in values)
        {
            WriteString(writer, value);
        }
    };

    public static Action<BinaryWriter> Int64(long value) => writer =>
    {
        writer.Write(40);
        writer.Write(value);
    };

    public static Action<BinaryWriter> Color(float r, float g, float b, float a) => writer =>
    {
        writer.Write(20);
        writer.Write(r);
        writer.Write(g);
        writer.Write(b);
        writer.Write(a);
    };

    /// <summary>An axis-aligned box: its lowest corner, then its size.</summary>
    public static Action<BinaryWriter> Aabb(Vector3 position, Vector3 size) => writer =>
    {
        writer.Write(15);
        foreach (var vector in new[] { position, size })
        {
            writer.Write(vector.X);
            writer.Write(vector.Y);
            writer.Write(vector.Z);
        }
    };

    public static Action<BinaryWriter> Array(params Action<BinaryWriter>[] items) => writer =>
    {
        writer.Write(30);
        writer.Write(items.Length);
        foreach (var item in items)
        {
            item(writer);
        }
    };

    public static Action<BinaryWriter> Dictionary(params (Action<BinaryWriter> Key, Action<BinaryWriter> Value)[] pairs) => writer =>
    {
        writer.Write(26);
        writer.Write(pairs.Length);
        foreach (var (key, value) in pairs)
        {
            key(writer);
            value(writer);
        }
    };

    /// <summary>A reference to the resource added at <paramref name="index"/> in this file.</summary>
    public static Action<BinaryWriter> Internal(int index) => writer =>
    {
        writer.Write(24);
        writer.Write(2);
        writer.Write(index);
    };

    public static Action<BinaryWriter> External(int index) => writer =>
    {
        writer.Write(24);
        writer.Write(3);
        writer.Write(index);
    };

    /// <summary>A node path with inline names (the top bit marks a name written in place).</summary>
    public static Action<BinaryWriter> NodePath(string[] names, string[] subNames) => writer =>
    {
        writer.Write(22);
        writer.Write((ushort)names.Length);
        writer.Write((ushort)subNames.Length);
        foreach (var name in names.Concat(subNames))
        {
            var bytes = Encoding.UTF8.GetBytes(name + "\0");
            writer.Write(0x80000000u | (uint)bytes.Length);
            writer.Write(bytes);
        }
    };

    private byte[] WriteHeader(string mainType, List<long> offsets, bool withoutMagic)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        if (!withoutMagic)
        {
            writer.Write("RSRC"u8);
        }

        writer.Write(0); // little-endian
        writer.Write(0); // 32-bit reals
        writer.Write(4);
        writer.Write(1);
        writer.Write(5);
        WriteString(writer, mainType);
        writer.Write(0L); // import metadata
        writer.Write(1); // named scene ids
        writer.Write(0L); // uid
        writer.Write(new byte[11 * 4]);
        writer.Write(_strings.Count);
        foreach (var value in _strings)
        {
            WriteString(writer, value);
        }

        writer.Write(_external.Count);
        foreach (var (type, path) in _external)
        {
            WriteString(writer, type);
            WriteString(writer, path);
        }

        writer.Write(_resources.Count);
        for (var index = 0; index < _resources.Count; index++)
        {
            WriteString(writer, "local://" + _resources[index].Id);
            writer.Write(offsets[index]);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
