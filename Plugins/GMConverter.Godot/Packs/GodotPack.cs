using System.Text;
using GMConverter.SDK.Common;

namespace GMConverter.Godot.Packs;

/// <summary>
/// A Godot resource pack (<c>.pck</c>): the game's files, either in a file of their own beside the
/// executable or appended to the executable itself. Reads the unencrypted packs of Godot 4.0 to 4.3
/// (format 2).
/// </summary>
internal sealed class GodotPack
{
    /// <summary>File extension of a stand-alone pack.</summary>
    public const string FileExtension = ".pck";

    private const uint _magic = 0x43504447; // "GDPC"
    private const int _encryptedDirectoryFlag = 1;
    private const int _reservedFieldCount = 16;

    private static readonly Dictionary<string, GodotPack> _packs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock _packsLock = new();

    private readonly Dictionary<string, GodotPackEntry> _entries;
    private readonly Lazy<IReadOnlyList<GodotRemap>> _remaps;

    private GodotPack(string path, Version engineVersion, IReadOnlyList<GodotPackEntry> entries)
    {
        FilePath = path;
        EngineVersion = engineVersion;
        Entries = entries;
        _entries = entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        _remaps = new Lazy<IReadOnlyList<GodotRemap>>(ReadRemaps);
    }

    /// <summary>The file holding the pack: the <c>.pck</c> or the executable it is appended to.</summary>
    public string FilePath { get; }

    /// <summary>The Godot version that exported the pack.</summary>
    public Version EngineVersion { get; }

    public IReadOnlyList<GodotPackEntry> Entries { get; }

    /// <summary>Whether <paramref name="path"/> holds a pack, at its start or appended to its end.</summary>
    public static bool Contains(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            return FindStart(stream, reader) is not null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The pack in <paramref name="path"/>, read once and shared.</summary>
    public static GodotPack For(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (_packsLock)
        {
            if (!_packs.TryGetValue(fullPath, out var pack))
            {
                pack = Open(fullPath);
                _packs[fullPath] = pack;
            }

            return pack;
        }
    }

    /// <summary>Forgets every pack read, so packs changed on disk are read again.</summary>
    public static void ClearAll()
    {
        lock (_packsLock)
        {
            _packs.Clear();
        }
    }

    public static GodotPack Open(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        var start = FindStart(stream, reader) ?? throw new GMConverterException($"No Godot pack in {path}.");

        stream.Position = start + 4;
        var formatVersion = reader.ReadUInt32();
        var engineVersion = new Version(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        if (formatVersion != 2)
        {
            throw new GMConverterException($"{path} is a Godot {engineVersion} pack (format {formatVersion}); only Godot 4.0 to 4.3 packs are supported.");
        }

        var flags = reader.ReadUInt32();
        var fileBase = reader.ReadInt64();
        if ((flags & _encryptedDirectoryFlag) != 0)
        {
            throw new GMConverterException($"{path} is an encrypted Godot pack.");
        }

        // The file base is where the files start in the host file, even when the pack is appended.
        stream.Position += _reservedFieldCount * sizeof(uint);

        var count = reader.ReadInt32();
        var entries = new GodotPackEntry[count];
        for (var i = 0; i < count; i++)
        {
            var name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32())).TrimEnd('\0');
            var offset = reader.ReadInt64();
            var size = reader.ReadInt64();
            stream.Position += 16; // MD5
            var fileFlags = reader.ReadUInt32();
            if ((fileFlags & 1) != 0)
            {
                throw new GMConverterException($"{path} holds encrypted files, which are not supported.");
            }

            entries[i] = new GodotPackEntry(Normalize(name), fileBase + offset, size);
        }

        return new GodotPack(path, engineVersion, entries);
    }

    /// <summary>The file at the resource path <paramref name="path"/>, or null when the pack lacks it.</summary>
    public GodotPackEntry? Find(string path)
    {
        return _entries.GetValueOrDefault(Normalize(path));
    }

    /// <summary>Every <c>.import</c> and <c>.remap</c> file in the pack, read once.</summary>
    public IReadOnlyList<GodotRemap> Remaps => _remaps.Value;

    public byte[] Read(GodotPackEntry entry)
    {
        using var stream = File.OpenRead(FilePath);
        return Read(stream, entry);
    }

    private GodotRemap[] ReadRemaps()
    {
        using var stream = File.OpenRead(FilePath);
        return [.. Entries
            .Where(entry => GodotRemap.IsRemapFile(entry.Path))
            .Select(entry => GodotRemap.Parse(entry.Path, Encoding.UTF8.GetString(Read(stream, entry))))];
    }

    private static byte[] Read(FileStream stream, GodotPackEntry entry)
    {
        stream.Position = entry.Offset;
        var buffer = new byte[entry.Size];
        stream.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>The file at the resource path <paramref name="path"/>.</summary>
    public byte[] Read(string path)
    {
        return Read(Find(path) ?? throw new GMConverterException($"{path} is not in the Godot pack."));
    }

    // Godot 4 writes "res://" paths; Godot 3 wrote them without the prefix.
    private static string Normalize(string path)
    {
        return path.StartsWith("res://", StringComparison.Ordinal) ? path : "res://" + path.TrimStart('/');
    }

    private static long? FindStart(Stream stream, BinaryReader reader)
    {
        if (stream.Length < 16)
        {
            return null;
        }

        if (reader.ReadUInt32() == _magic)
        {
            return 0;
        }

        // An appended pack ends with its size and the magic.
        stream.Position = stream.Length - 12;
        var size = reader.ReadInt64();
        if (reader.ReadUInt32() != _magic)
        {
            return null;
        }

        var start = stream.Length - 12 - size;
        if (start < 0)
        {
            return null;
        }

        stream.Position = start;
        return reader.ReadUInt32() == _magic ? start : null;
    }
}
