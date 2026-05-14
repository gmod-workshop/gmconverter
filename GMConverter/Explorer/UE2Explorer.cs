using GMConverter.Common;
using GMConverter.Formats.Unreal;

namespace GMConverter.Explorer;

internal sealed class UE2Explorer : IExplorer
{
    private static readonly HashSet<string> _packageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ukx",
        ".usx",
        ".utx",
        ".uax",
        ".u",
        ".umx",
        ".unr",
        ".ctm",
        ".upx"
    };

    private static readonly HashSet<string> _meshClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SkeletalMesh",
        "StaticMesh"
    };

    private static readonly HashSet<string> _gameDataChildDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Animations",
        "Maps",
        "Sounds",
        "StaticMeshes",
        "Textures",
        "System",
        "Music"
    };

    private readonly Dictionary<string, UnrealPackageFile?> _packageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    public string Id => "ue2";

    public string DisplayName => "Unreal Engine 2 packages";

    public bool Supports(ExplorerTarget target)
    {
        if (target.IsFile)
        {
            return IsCandidatePackage(target.FullPath) && UnrealPackageFile.HasPackageTag(target.FullPath);
        }

        return target.IsDirectory && ExplorerFileSystem.EnumerateFiles(target.FullPath)
            .Any(file => IsCandidatePackage(file) && UnrealPackageFile.HasPackageTag(file));
    }

    public IReadOnlyList<ExplorerFileEntry> Scan(ExplorerTarget target)
    {
        if (target.IsFile)
        {
            var root = GetPackageSearchRoot(target.FullPath);
            return EnumeratePackageEntries(root, target.FullPath)
                .OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return ExplorerFileSystem.EnumerateFiles(target.FullPath)
            .Where(IsCandidatePackage)
            .SelectMany(file => EnumeratePackageEntries(target.FullPath, file))
            .OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ExplorerResolvedEntry ResolveEntry(ExplorerFileEntry fileEntry)
    {
        if (fileEntry.ArchivePath is null ||
            fileEntry.ArchiveEntryPath is null ||
            fileEntry.AssetClass is null)
        {
            throw new GMConverterException("UE2 package entry is missing export metadata.");
        }

        var package = GetPackage(fileEntry.ArchivePath)
            ?? throw new GMConverterException($"Unable to read UE2 package: {fileEntry.ArchivePath}");
        var extractionRoot = GetExportRoot(fileEntry);
        Directory.CreateDirectory(extractionRoot);

        var export = package.Exports.FirstOrDefault(item =>
            package.GetClassName(item).Equals(fileEntry.AssetClass, StringComparison.OrdinalIgnoreCase) &&
            package.GetFullExportName(item).Equals(fileEntry.ArchiveEntryPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new GMConverterException($"UE2 export not found: {fileEntry.ArchiveEntryPath}");
        var result = UnrealActorXExporter.ExportMesh(package, export, extractionRoot, fileEntry.SearchRoot);

        return new ExplorerResolvedEntry(result.MeshPath, extractionRoot, result.AnimationPath);
    }

    public void ClearCaches()
    {
        lock (_cacheLock)
        {
            _packageCache.Clear();
        }
    }

    private IEnumerable<ExplorerFileEntry> EnumeratePackageEntries(string root, string packagePath)
    {
        var package = GetPackage(packagePath);
        if (package is null)
        {
            yield break;
        }

        var packageRelativePath = Path.GetRelativePath(root, packagePath).Replace(Path.DirectorySeparatorChar, '/');
        foreach (var export in package.Exports.Where(export => export.SerialSize > 0))
        {
            var className = package.GetClassName(export);
            if (!_meshClasses.Contains(className))
            {
                continue;
            }

            var objectName = package.GetFullExportName(export);
            var displayPath = $"{packageRelativePath}/{SanitizePathSegment(className)}/{SanitizeObjectPath(objectName)}";
            var details = $"Class {className} | Object {objectName} | Version {package.Summary.FileVersion}/{package.Summary.LicenseeVersion} | Offset 0x{export.SerialOffset:X} | Size {export.SerialSize:N0}";

            yield return new ExplorerFileEntry(
                displayPath,
                packagePath,
                "psk",
                root,
                root,
                packagePath,
                objectName,
                Id,
                details,
                AssetClass: className);
        }
    }

    private static string GetExportRoot(ExplorerFileEntry fileEntry)
    {
        var archivePath = fileEntry.ArchivePath ?? fileEntry.FilePath;
        var exportKey = $"{Path.GetFullPath(archivePath)}|{fileEntry.AssetClass}|{fileEntry.ArchiveEntryPath}";
        var exportHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(exportKey)))[..12];
        return Path.Combine(ExplorerFileSystem.GetExtractionRoot(archivePath), "__ue2_exports", exportHash);
    }

    private UnrealPackageFile? GetPackage(string packagePath)
    {
        var cacheKey = PackageCacheKey(packagePath);
        lock (_cacheLock)
        {
            if (_packageCache.TryGetValue(cacheKey, out var package))
            {
                return package;
            }
        }

        UnrealPackageFile? loadedPackage;
        try
        {
            loadedPackage = UnrealPackageFile.Read(packagePath);
        }
        catch (GMConverterException)
        {
            loadedPackage = null;
        }
        catch (IOException)
        {
            loadedPackage = null;
        }
        catch (UnauthorizedAccessException)
        {
            loadedPackage = null;
        }

        lock (_cacheLock)
        {
            _packageCache[cacheKey] = loadedPackage;
        }

        return loadedPackage;
    }

    private static string PackageCacheKey(string packagePath)
    {
        var fullPath = Path.GetFullPath(packagePath);
        try
        {
            var fileInfo = new FileInfo(fullPath);
            return $"{fullPath}|{fileInfo.LastWriteTimeUtc.Ticks}|{fileInfo.Length}";
        }
        catch (IOException)
        {
            return fullPath;
        }
        catch (UnauthorizedAccessException)
        {
            return fullPath;
        }
    }

    private static bool IsCandidatePackage(string file)
    {
        return _packageExtensions.Contains(Path.GetExtension(file));
    }

    private static string GetPackageSearchRoot(string packagePath)
    {
        var packageDirectory = Path.GetDirectoryName(packagePath) ?? Environment.CurrentDirectory;
        var directoryName = Path.GetFileName(packageDirectory);
        if (_gameDataChildDirectories.Contains(directoryName))
        {
            return Directory.GetParent(packageDirectory)?.FullName ?? packageDirectory;
        }

        return packageDirectory;
    }

    private static string SanitizeObjectPath(string objectName)
    {
        return string.Join('/', objectName
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizePathSegment));
    }

    private static string SanitizePathSegment(string value)
    {
        var sanitized = string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_')).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "Object" : sanitized;
    }
}
