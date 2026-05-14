using System.Text;
using GMConverter.Common;

namespace GMConverter.Formats.Unreal;

internal sealed record UnrealPackageFile(
    string FilePath,
    UnrealPackageSummary Summary,
    IReadOnlyList<string> Names,
    IReadOnlyList<UnrealPackageImport> Imports,
    IReadOnlyList<UnrealPackageExport> Exports)
{
    private const uint _packageFileTag = 0x9E2A83C1;
    private const uint _packageFileTagReversed = 0xC1832A9E;
    private const int _maxNameLength = 1024;
    private const int _maxTableCount = 1_000_000;

    public static bool HasPackageTag(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            if (stream.Length < sizeof(uint))
            {
                return false;
            }

            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            return reader.ReadUInt32() is _packageFileTag or _packageFileTagReversed;
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

    public static UnrealPackageFile Read(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        var summary = ReadSummary(reader, stream.Length);
        var names = ReadNameTable(reader, summary, stream.Length);
        var imports = ReadImportTable(reader, summary, names, stream.Length);
        var exports = ReadExportTable(reader, summary, names, stream.Length);

        return new UnrealPackageFile(filePath, summary, names, imports, exports);
    }

    public string GetClassName(UnrealPackageExport export)
    {
        return GetObjectName(export.ClassIndex);
    }

    public string GetFullExportName(UnrealPackageExport export)
    {
        List<string> segments = [];
        AddOuterNames(export.PackageIndex, segments);
        segments.Add(export.ObjectName);

        return string.Join('.', segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    public string GetFullImportName(UnrealPackageImport import)
    {
        List<string> segments = [];
        AddOuterNames(import.PackageIndex, segments);
        segments.Add(import.ObjectName);

        return string.Join('.', segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    public UnrealPackageExport? GetExport(int packageIndex)
    {
        if (packageIndex <= 0)
        {
            return null;
        }

        var exportIndex = packageIndex - 1;
        return (uint)exportIndex < Exports.Count ? Exports[exportIndex] : null;
    }

    public UnrealPackageImport? GetImport(int packageIndex)
    {
        if (packageIndex >= 0)
        {
            return null;
        }

        var importIndex = -packageIndex - 1;
        return (uint)importIndex < Imports.Count ? Imports[importIndex] : null;
    }

    public string GetObjectName(int packageIndex)
    {
        if (packageIndex < 0)
        {
            var import = GetImport(packageIndex);
            return import?.ObjectName ?? "Import";
        }

        if (packageIndex > 0)
        {
            var export = GetExport(packageIndex);
            return export?.ObjectName ?? "Export";
        }

        return "Class";
    }

    public UnrealPackageExport? FindExport(string objectName, string? className)
    {
        return Exports.FirstOrDefault(export =>
            export.ObjectName.Equals(objectName, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(className) || GetClassName(export).Equals(className, StringComparison.OrdinalIgnoreCase))) ??
            Exports.FirstOrDefault(export => export.ObjectName.Equals(objectName, StringComparison.OrdinalIgnoreCase));
    }

    private static UnrealPackageSummary ReadSummary(BinaryReader reader, long fileLength)
    {
        var tag = reader.ReadUInt32();
        if (tag == _packageFileTagReversed)
        {
            throw new GMConverterException("Big-endian Unreal packages are not supported yet.");
        }

        if (tag != _packageFileTag)
        {
            throw new GMConverterException("Not an Unreal package file.");
        }

        var version = reader.ReadUInt32();
        if ((version & 0xFFFFF000) == 0xFFFFF000)
        {
            throw new GMConverterException("UE4 package files are not supported by the UE2 package reader.");
        }

        var fileVersion = (int)(version & 0xFFFF);
        var licenseeVersion = (int)(version >> 16);
        var packageFlags = reader.ReadInt32();
        var nameCount = reader.ReadInt32();
        var nameOffset = reader.ReadInt32();
        var exportCount = reader.ReadInt32();
        var exportOffset = reader.ReadInt32();
        var importCount = reader.ReadInt32();
        var importOffset = reader.ReadInt32();

        ValidateTable("name", nameCount, nameOffset, fileLength);
        ValidateTable("export", exportCount, exportOffset, fileLength);
        ValidateTable("import", importCount, importOffset, fileLength);

        Guid guid;
        List<UnrealPackageGeneration> generations = [];
        if (fileVersion < 68)
        {
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            guid = Guid.Empty;
            generations.Add(new UnrealPackageGeneration(exportCount, nameCount));
        }
        else
        {
            guid = new Guid(reader.ReadBytes(16));
            var generationCount = reader.ReadInt32();
            if (generationCount is < 0 or > 1024)
            {
                throw new GMConverterException("Unreal package generation count is invalid.");
            }

            for (var i = 0; i < generationCount; i++)
            {
                generations.Add(new UnrealPackageGeneration(reader.ReadInt32(), reader.ReadInt32()));
            }
        }

        return new UnrealPackageSummary(
            fileVersion,
            licenseeVersion,
            packageFlags,
            nameCount,
            nameOffset,
            exportCount,
            exportOffset,
            importCount,
            importOffset,
            guid,
            generations);
    }

    private static List<string> ReadNameTable(
        BinaryReader reader,
        UnrealPackageSummary summary,
        long fileLength)
    {
        reader.BaseStream.Seek(summary.NameOffset, SeekOrigin.Begin);
        List<string> names = new(summary.NameCount);

        for (var i = 0; i < summary.NameCount; i++)
        {
            EnsureReadable(reader, fileLength, 1);
            string name = summary.FileVersion < 64
                ? ReadNullTerminatedString(reader)
                : ReadUnrealString(reader);

            _ = reader.ReadUInt32();
            names.Add(name);
        }

        return names;
    }

    private static List<UnrealPackageImport> ReadImportTable(
        BinaryReader reader,
        UnrealPackageSummary summary,
        IReadOnlyList<string> names,
        long fileLength)
    {
        reader.BaseStream.Seek(summary.ImportOffset, SeekOrigin.Begin);
        List<UnrealPackageImport> imports = new(summary.ImportCount);

        for (var i = 0; i < summary.ImportCount; i++)
        {
            EnsureReadable(reader, fileLength, 1);
            imports.Add(new UnrealPackageImport(
                ReadName(reader, names),
                ReadName(reader, names),
                reader.ReadInt32(),
                ReadName(reader, names)));
        }

        return imports;
    }

    private static List<UnrealPackageExport> ReadExportTable(
        BinaryReader reader,
        UnrealPackageSummary summary,
        IReadOnlyList<string> names,
        long fileLength)
    {
        var republicCommandoExports = TryReadExportTable(
            reader,
            summary,
            names,
            fileLength,
            UnrealPackageExportLayout.RepublicCommando);
        var genericExports = TryReadExportTable(
            reader,
            summary,
            names,
            fileLength,
            UnrealPackageExportLayout.Generic);

        return ScoreExports(republicCommandoExports, fileLength) >= ScoreExports(genericExports, fileLength)
            ? republicCommandoExports
            : genericExports;
    }

    private static List<UnrealPackageExport> TryReadExportTable(
        BinaryReader reader,
        UnrealPackageSummary summary,
        IReadOnlyList<string> names,
        long fileLength,
        UnrealPackageExportLayout layout)
    {
        try
        {
            reader.BaseStream.Seek(summary.ExportOffset, SeekOrigin.Begin);
            List<UnrealPackageExport> exports = new(summary.ExportCount);

            for (var i = 0; i < summary.ExportCount; i++)
            {
                EnsureReadable(reader, fileLength, 1);
                exports.Add(layout is UnrealPackageExportLayout.RepublicCommando
                    ? ReadRepublicCommandoExport(reader, summary, names)
                    : ReadGenericExport(reader, names));
            }

            return exports;
        }
        catch (EndOfStreamException)
        {
            return [];
        }
        catch (GMConverterException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static UnrealPackageExport ReadGenericExport(BinaryReader reader, IReadOnlyList<string> names)
    {
        var classIndex = ReadCompactIndex(reader);
        var superIndex = ReadCompactIndex(reader);
        var packageIndex = reader.ReadInt32();
        var objectName = ReadName(reader, names);
        var objectFlags = reader.ReadUInt32();
        var serialSize = ReadCompactIndex(reader);
        var serialOffset = serialSize == 0 ? 0 : ReadCompactIndex(reader);

        return new UnrealPackageExport(
            classIndex,
            superIndex,
            packageIndex,
            objectName,
            objectFlags,
            serialSize,
            serialOffset);
    }

    private static UnrealPackageExport ReadRepublicCommandoExport(
        BinaryReader reader,
        UnrealPackageSummary summary,
        IReadOnlyList<string> names)
    {
        var classIndex = ReadCompactIndex(reader);
        var superIndex = ReadCompactIndex(reader);
        var packageIndex = reader.ReadInt32();
        if (summary.FileVersion >= 159)
        {
            _ = ReadCompactIndex(reader);
        }

        var objectName = ReadName(reader, names);
        var objectFlags = reader.ReadUInt32();
        var serialSize = reader.ReadInt32();
        var serialOffset = reader.ReadInt32();

        return new UnrealPackageExport(
            classIndex,
            superIndex,
            packageIndex,
            objectName,
            objectFlags,
            serialSize,
            serialOffset);
    }

    private static int ScoreExports(IReadOnlyList<UnrealPackageExport> exports, long fileLength)
    {
        return exports.Sum(export =>
        {
            var score = 0;
            if (!string.IsNullOrWhiteSpace(export.ObjectName))
            {
                score += 2;
            }

            if (export.SerialSize >= 0 &&
                export.SerialOffset >= 0 &&
                export.SerialOffset <= fileLength &&
                export.SerialSize <= fileLength &&
                export.SerialOffset + (long)export.SerialSize <= fileLength)
            {
                score += 2;
            }

            if (export.ClassIndex != 0)
            {
                score++;
            }

            return score;
        });
    }

    private static string ReadName(BinaryReader reader, IReadOnlyList<string> names)
    {
        var nameIndex = ReadCompactIndex(reader);
        if ((uint)nameIndex >= names.Count)
        {
            throw new GMConverterException($"Unreal package name index is invalid: {nameIndex}");
        }

        return names[nameIndex];
    }

    private static int ReadCompactIndex(BinaryReader reader)
    {
        var firstByte = reader.ReadByte();
        var negative = (firstByte & 0x80) != 0;
        var value = firstByte & 0x3F;
        if ((firstByte & 0x40) != 0)
        {
            var shift = 6;

            for (var i = 0; i < 4; i++)
            {
                firstByte = reader.ReadByte();
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

    private static string ReadUnrealString(BinaryReader reader)
    {
        var count = ReadCompactIndex(reader);
        if (count == 0)
        {
            return string.Empty;
        }

        if (count > 0)
        {
            if (count > _maxNameLength)
            {
                throw new GMConverterException("Unreal package string is too long.");
            }

            var bytes = reader.ReadBytes(count);
            if (bytes.Length != count)
            {
                throw new EndOfStreamException();
            }

            return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        }

        var characterCount = -count;
        if (characterCount > _maxNameLength)
        {
            throw new GMConverterException("Unreal package string is too long.");
        }

        var stringBytes = reader.ReadBytes(characterCount * sizeof(char));
        if (stringBytes.Length != characterCount * sizeof(char))
        {
            throw new EndOfStreamException();
        }

        return Encoding.Unicode.GetString(stringBytes).TrimEnd('\0');
    }

    private static string ReadNullTerminatedString(BinaryReader reader)
    {
        List<byte> bytes = [];
        for (var i = 0; i < _maxNameLength; i++)
        {
            var value = reader.ReadByte();
            if (value == 0)
            {
                return Encoding.ASCII.GetString([.. bytes]);
            }

            bytes.Add(value);
        }

        throw new GMConverterException("Unreal package string is too long.");
    }

    private static void ValidateTable(string tableName, int count, int offset, long fileLength)
    {
        if (count is < 0 or > _maxTableCount ||
            offset < 0 ||
            offset > fileLength)
        {
            throw new GMConverterException($"Unreal package {tableName} table is invalid.");
        }
    }

    private static void EnsureReadable(BinaryReader reader, long fileLength, int byteCount)
    {
        if (reader.BaseStream.Position + byteCount > fileLength)
        {
            throw new EndOfStreamException();
        }
    }

    private void AddOuterNames(int packageIndex, List<string> segments)
    {
        if (packageIndex > 0)
        {
            var export = GetExport(packageIndex);
            if (export is not null)
            {
                AddOuterNames(export.PackageIndex, segments);
                segments.Add(export.ObjectName);
            }
        }
        else if (packageIndex < 0)
        {
            var import = GetImport(packageIndex);
            if (import is not null)
            {
                AddOuterNames(import.PackageIndex, segments);
                segments.Add(import.ObjectName);
            }
        }
    }

    private enum UnrealPackageExportLayout
    {
        Generic,
        RepublicCommando
    }
}

internal sealed record UnrealPackageSummary(
    int FileVersion,
    int LicenseeVersion,
    int PackageFlags,
    int NameCount,
    int NameOffset,
    int ExportCount,
    int ExportOffset,
    int ImportCount,
    int ImportOffset,
    Guid Guid,
    IReadOnlyList<UnrealPackageGeneration> Generations);

internal sealed record UnrealPackageGeneration(int ExportCount, int NameCount);

internal sealed record UnrealPackageImport(
    string ClassPackage,
    string ClassName,
    int PackageIndex,
    string ObjectName);

internal sealed record UnrealPackageExport(
    int ClassIndex,
    int SuperIndex,
    int PackageIndex,
    string ObjectName,
    uint ObjectFlags,
    int SerialSize,
    int SerialOffset);
