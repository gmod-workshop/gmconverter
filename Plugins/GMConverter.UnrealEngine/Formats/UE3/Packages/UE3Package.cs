using System.Buffers.Binary;
using System.Text;
using GMConverter.SDK.Common;

namespace GMConverter.UnrealEngine.Formats.UE3.Packages;

/// <summary>
/// An Unreal Engine 3 package. Reading one decompresses only its tables; the object data is
/// decompressed the first time an export is read.
/// </summary>
internal sealed class UE3Package
{
    // UE2 packages stop well below 200 and UE4 stores a negative legacy version here instead.
    private const int _minimumVersion = 200;
    private const int _maximumVersion = 1000;
    private const int _maximumNameLength = 1024;

    private readonly object _dataLock = new();
    private byte[]? _data;

    private UE3Package(
        string filePath,
        UE3PackageSummary summary,
        IReadOnlyList<string> names,
        IReadOnlyList<UE3PackageImport> imports,
        IReadOnlyList<UE3PackageExport> exports)
    {
        FilePath = filePath;
        Summary = summary;
        Names = names;
        Imports = imports;
        Exports = exports;
    }

    public string FilePath { get; }

    public UE3PackageSummary Summary { get; }

    public IReadOnlyList<string> Names { get; }

    public IReadOnlyList<UE3PackageImport> Imports { get; }

    public IReadOnlyList<UE3PackageExport> Exports { get; }

    /// <summary>True when the file starts like a UE3 package (and not a texture cache, which holds bare compressed chunks).</summary>
    public static bool IsPackage(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            Span<byte> header = stackalloc byte[8];
            if (stream.Read(header) < header.Length || BinaryPrimitives.ReadUInt32LittleEndian(header) != UE3Compression.PackageTag)
            {
                return false;
            }

            return IsPackageVersion(BinaryPrimitives.ReadUInt16LittleEndian(header[4..]));
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

    public static bool IsPackageVersion(int fileVersion) => fileVersion is >= _minimumVersion and < _maximumVersion;

    public static UE3Package Read(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var summary = ReadSummary(stream);
        var header = ReadHeaderBytes(stream, summary);
        var archive = new UE3Archive(header, []);
        var names = ReadNames(archive, summary);
        archive = new UE3Archive(header, names, summary.ImportOffset);
        var imports = ReadImports(archive, summary);
        archive.Position = summary.ExportOffset;
        var exports = ReadExports(archive, summary);
        return new UE3Package(filePath, summary, names, imports, exports);
    }

    /// <summary>A reader positioned at the start of <paramref name="export"/>'s data.</summary>
    public UE3Archive OpenExport(UE3PackageExport export)
    {
        var data = GetData();
        if (export.SerialOffset < 0 || export.SerialSize < 0 || export.SerialOffset + export.SerialSize > data.Length)
        {
            throw new GMConverterException($"UE3 export {export.ObjectName} lies outside its package {FilePath}.");
        }

        return new UE3Archive(data, Names, export.SerialOffset);
    }

    /// <summary>
    /// Reads an object's tagged properties, leaving the archive at the class's own (native) data
    /// that follows them.
    /// </summary>
    public (UE3Archive Archive, UE3Properties Properties) OpenObject(UE3PackageExport export)
    {
        var archive = OpenExport(export);
        if (Summary.FileVersion >= 322)
        {
            archive.ReadInt32(); // net index
        }

        return (archive, UE3Properties.Read(archive, Summary.FileVersion));
    }

    /// <summary>
    /// Opens a component (a UComponent subclass such as a particle distribution), which stores the
    /// class that owns its template between the net index and its properties.
    /// </summary>
    public (UE3Archive Archive, UE3Properties Properties) OpenComponent(UE3PackageExport export)
    {
        var archive = OpenExport(export);
        if (Summary.FileVersion >= 322)
        {
            archive.ReadInt32(); // net index
        }

        archive.ReadInt32(); // template owner class
        return (archive, UE3Properties.Read(archive, Summary.FileVersion));
    }

    /// <summary>The tagged properties of a struct property, such as a material expression input.</summary>
    public UE3Properties ReadStruct(UE3Property property)
    {
        return UE3Properties.Read(new UE3Archive(GetData(), Names, property.ValueOffset), Summary.FileVersion);
    }

    /// <summary>A slice of the decompressed package, such as bulk data stored inline.</summary>
    public ReadOnlyMemory<byte> GetBytes(int offset, int count)
    {
        var data = GetData();
        if (offset < 0 || count < 0 || offset + count > data.Length)
        {
            throw new GMConverterException($"UE3 data at 0x{offset:X} lies outside its package {FilePath}.");
        }

        return data.AsMemory(offset, count);
    }

    public UE3PackageExport? GetExport(int packageIndex)
    {
        return packageIndex > 0 && packageIndex <= Exports.Count ? Exports[packageIndex - 1] : null;
    }

    public UE3PackageImport? GetImport(int packageIndex)
    {
        return packageIndex < 0 && -packageIndex <= Imports.Count ? Imports[-packageIndex - 1] : null;
    }

    public int GetExportIndex(UE3PackageExport export)
    {
        for (var index = 0; index < Exports.Count; index++)
        {
            if (ReferenceEquals(Exports[index], export))
            {
                return index + 1;
            }
        }

        return 0;
    }

    public string GetObjectName(int packageIndex)
    {
        return GetExport(packageIndex)?.ObjectName ?? GetImport(packageIndex)?.ObjectName ?? "None";
    }

    public string GetClassName(UE3PackageExport export)
    {
        return export.ClassIndex == 0 ? "Class" : GetObjectName(export.ClassIndex);
    }

    public string GetClassName(int packageIndex)
    {
        return GetExport(packageIndex) is { } export ? GetClassName(export) : GetImport(packageIndex)?.ClassName ?? "None";
    }

    /// <summary>The object's dotted path through its outers, such as <c>Pkg.Group.Object</c>.</summary>
    public string GetPath(int packageIndex)
    {
        List<string> segments = [];
        var guard = 0;
        while (packageIndex != 0 && guard++ < 64)
        {
            if (GetExport(packageIndex) is { } export)
            {
                segments.Add(export.ObjectName);
                packageIndex = export.OuterIndex;
            }
            else if (GetImport(packageIndex) is { } import)
            {
                segments.Add(import.ObjectName);
                packageIndex = import.OuterIndex;
            }
            else
            {
                break;
            }
        }

        segments.Reverse();
        return string.Join('.', segments);
    }

    public UE3PackageExport? FindExport(string objectName, string className)
    {
        return Exports.FirstOrDefault(export =>
            export.ObjectName.Equals(objectName, StringComparison.OrdinalIgnoreCase) &&
            GetClassName(export).Equals(className, StringComparison.OrdinalIgnoreCase));
    }

    private byte[] GetData()
    {
        lock (_dataLock)
        {
            if (_data is not null)
            {
                return _data;
            }

            using var stream = File.OpenRead(FilePath);
            if (Summary.Chunks.Count == 0)
            {
                _data = new byte[stream.Length];
                stream.ReadExactly(_data);
                return _data;
            }

            var method = UE3Compression.FromPackageFlags(Summary.CompressionFlags);
            var size = Summary.Chunks.Max(chunk => chunk.UncompressedOffset + chunk.UncompressedSize);
            var data = new byte[size];
            stream.Seek(0, SeekOrigin.Begin);
            stream.ReadExactly(data.AsSpan(0, Summary.Chunks[0].UncompressedOffset));
            foreach (var chunk in Summary.Chunks)
            {
                UE3Compression.ReadChunk(stream, chunk.CompressedOffset, method).CopyTo(data.AsSpan(chunk.UncompressedOffset));
            }

            _data = data;
            return _data;
        }
    }

    private static UE3PackageSummary ReadSummary(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen: true);
        if (reader.ReadUInt32() != UE3Compression.PackageTag)
        {
            throw new GMConverterException("Not an Unreal package: the package tag is missing.");
        }

        var fileVersion = reader.ReadUInt16();
        var licenseeVersion = reader.ReadUInt16();
        if (!IsPackageVersion(fileVersion))
        {
            throw new GMConverterException($"Unreal package version {fileVersion} is not Unreal Engine 3.");
        }

        var headerSize = reader.ReadInt32();
        var folderLength = reader.ReadInt32();
        stream.Seek(folderLength >= 0 ? folderLength : -folderLength * 2L, SeekOrigin.Current);
        var packageFlags = reader.ReadUInt32();
        var nameCount = reader.ReadInt32();
        var nameOffset = reader.ReadInt32();
        var exportCount = reader.ReadInt32();
        var exportOffset = reader.ReadInt32();
        var importCount = reader.ReadInt32();
        var importOffset = reader.ReadInt32();
        var dependsOffset = reader.ReadInt32();
        if (fileVersion >= 623)
        {
            reader.ReadInt32(); // import/export guids offset
            reader.ReadInt32(); // import guid count
            reader.ReadInt32(); // export guid count
        }

        if (fileVersion >= 584)
        {
            reader.ReadInt32(); // thumbnail table offset
        }

        reader.ReadBytes(16); // package guid
        var generationCount = reader.ReadInt32();
        if (generationCount is < 0 or > 10_000)
        {
            throw new GMConverterException("UE3 package has an invalid generation count.");
        }

        stream.Seek(generationCount * (fileVersion >= 322 ? 12L : 8L), SeekOrigin.Current);
        var engineVersion = fileVersion >= 245 ? reader.ReadInt32() : 0;
        if (fileVersion >= 277)
        {
            reader.ReadInt32(); // cooker version
        }

        var compressionFlags = 0u;
        List<UE3CompressedChunk> chunks = [];
        if (fileVersion >= 334)
        {
            compressionFlags = reader.ReadUInt32();
            var chunkCount = reader.ReadInt32();
            if (chunkCount is < 0 or > 100_000)
            {
                throw new GMConverterException("UE3 package has an invalid compressed chunk count.");
            }

            for (var index = 0; index < chunkCount; index++)
            {
                chunks.Add(new UE3CompressedChunk(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
            }
        }

        return new UE3PackageSummary(
            fileVersion,
            licenseeVersion,
            headerSize,
            packageFlags,
            nameCount,
            nameOffset,
            exportCount,
            exportOffset,
            importCount,
            importOffset,
            dependsOffset,
            engineVersion,
            compressionFlags,
            chunks);
    }

    // The tables sit in the first chunks; only those are decompressed.
    private static byte[] ReadHeaderBytes(Stream stream, UE3PackageSummary summary)
    {
        var tableEnd = Math.Max(summary.HeaderSize, summary.DependsOffset);
        if (summary.Chunks.Count == 0)
        {
            var length = (int)Math.Min(stream.Length, Math.Max(tableEnd, 0));
            var raw = new byte[length];
            stream.Seek(0, SeekOrigin.Begin);
            stream.ReadExactly(raw);
            return raw;
        }

        var method = UE3Compression.FromPackageFlags(summary.CompressionFlags);
        var headerChunks = summary.Chunks.Where(chunk => chunk.UncompressedOffset < tableEnd).ToArray();
        var size = headerChunks.Length == 0
            ? summary.Chunks[0].UncompressedOffset
            : headerChunks.Max(chunk => chunk.UncompressedOffset + chunk.UncompressedSize);
        var data = new byte[Math.Max(size, summary.Chunks[0].UncompressedOffset)];
        stream.Seek(0, SeekOrigin.Begin);
        stream.ReadExactly(data.AsSpan(0, summary.Chunks[0].UncompressedOffset));
        foreach (var chunk in headerChunks)
        {
            UE3Compression.ReadChunk(stream, chunk.CompressedOffset, method).CopyTo(data.AsSpan(chunk.UncompressedOffset));
        }

        return data;
    }

    private static List<string> ReadNames(UE3Archive archive, UE3PackageSummary summary)
    {
        // CrossFire HD pads each name entry with (length + 4) & 7 junk bytes before its flags.
        // Stock packages have none; whichever layout ends exactly where the import table starts wins.
        foreach (var padded in new[] { false, true })
        {
            archive.Position = summary.NameOffset;
            if (TryReadNames(archive, summary, padded) is { } names &&
                (summary.ImportOffset <= summary.NameOffset || archive.Position == summary.ImportOffset))
            {
                return names;
            }
        }

        throw new GMConverterException("UE3 name table could not be read.");
    }

    private static List<string>? TryReadNames(UE3Archive archive, UE3PackageSummary summary, bool padded)
    {
        try
        {
            List<string> names = new(summary.NameCount);
            for (var index = 0; index < summary.NameCount; index++)
            {
                var length = archive.ReadInt32();
                if (length == 0 || Math.Abs(length) > _maximumNameLength)
                {
                    return null;
                }

                archive.Position -= 4;
                var name = archive.ReadString();
                if (padded)
                {
                    archive.Skip((name.Length + 4) & 7);
                }

                archive.Skip(summary.FileVersion >= 195 ? 8 : 4);
                names.Add(name);
            }

            return names;
        }
        catch (GMConverterException)
        {
            return null;
        }
    }

    private static List<UE3PackageImport> ReadImports(UE3Archive archive, UE3PackageSummary summary)
    {
        List<UE3PackageImport> imports = new(summary.ImportCount);
        for (var index = 0; index < summary.ImportCount; index++)
        {
            imports.Add(new UE3PackageImport(archive.ReadName(), archive.ReadName(), archive.ReadInt32(), archive.ReadName()));
        }

        return imports;
    }

    private static List<UE3PackageExport> ReadExports(UE3Archive archive, UE3PackageSummary summary)
    {
        var version = summary.FileVersion;
        List<UE3PackageExport> exports = new(summary.ExportCount);
        for (var index = 0; index < summary.ExportCount; index++)
        {
            var classIndex = archive.ReadInt32();
            var superIndex = archive.ReadInt32();
            var outerIndex = archive.ReadInt32();
            var objectName = archive.ReadName();
            var archetypeIndex = version >= 220 ? archive.ReadInt32() : 0;
            var objectFlags = version >= 195 ? archive.ReadUInt64() : archive.ReadUInt32();
            var serialSize = archive.ReadInt32();
            var serialOffset = serialSize != 0 || version >= 249 ? archive.ReadInt32() : 0;
            if (version < 543)
            {
                // Component map: name -> object index pairs.
                var componentCount = archive.ReadCount();
                archive.Skip(componentCount * 12);
            }

            if (version >= 247)
            {
                archive.ReadUInt32(); // export flags
            }

            if (version >= 322)
            {
                var generationCount = archive.ReadCount();
                archive.Skip(generationCount * 4);
                archive.Skip(16); // package guid
            }

            if (version >= 475)
            {
                archive.ReadUInt32(); // package flags
            }

            exports.Add(new UE3PackageExport(classIndex, superIndex, outerIndex, objectName, archetypeIndex, objectFlags, serialSize, serialOffset));
        }

        return exports;
    }
}
