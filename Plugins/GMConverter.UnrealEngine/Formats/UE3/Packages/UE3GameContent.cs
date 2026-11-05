using System.Buffers.Binary;
using System.Collections.Concurrent;
using GMConverter.SDK.Common;
using GMConverter.SDK.Explorer;
using GMConverter.UnrealEngine.Formats.UE3.Textures;

namespace GMConverter.UnrealEngine.Formats.UE3.Packages;

/// <summary>
/// A UE3 game's content folder: its packages and texture caches. Objects reference other packages
/// by package name, which a cooked game need not use as a file name (CrossFire HD names its files
/// by a hash), so references are followed through an index of every package's top-level objects.
/// </summary>
internal sealed class UE3GameContent
{
    private readonly ConcurrentDictionary<string, UE3Package> _packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _indexLock = new();
    private Dictionary<string, List<string>>? _topLevelObjects;

    private UE3GameContent(string root, IReadOnlyList<string> packagePaths, IReadOnlyList<string> cachePaths)
    {
        Root = root;
        PackagePaths = packagePaths;
        TextureCaches = new UE3TextureCaches(cachePaths);
    }

    public string Root { get; }

    public IReadOnlyList<string> PackagePaths { get; }

    public UE3TextureCaches TextureCaches { get; }

    /// <summary>Sorts the files under <paramref name="root"/> into packages and texture caches.</summary>
    public static UE3GameContent Open(string root)
    {
        List<string> packages = [];
        List<string> caches = [];
        foreach (var file in ExplorerFileSystem.EnumerateFiles(root))
        {
            switch (Classify(file))
            {
                case FileKind.Package:
                    packages.Add(file);
                    break;
                case FileKind.Cache:
                    caches.Add(file);
                    break;
            }
        }

        packages.Sort(StringComparer.OrdinalIgnoreCase);
        caches.Sort(StringComparer.OrdinalIgnoreCase);
        return new UE3GameContent(root, packages, caches);
    }

    public UE3Package GetPackage(string path)
    {
        return _packages.GetOrAdd(Path.GetFullPath(path), UE3Package.Read);
    }

    /// <summary>
    /// Reads every package's tables once, in parallel, handing each to <paramref name="visit"/> and
    /// indexing its top-level objects. Packages that fail to read are skipped.
    /// </summary>
    public void Scan(Action<UE3Package>? visit = null)
    {
        ConcurrentDictionary<string, ConcurrentBag<string>> topLevel = new(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(PackagePaths, path =>
        {
            UE3Package package;
            try
            {
                package = UE3Package.Read(path);
            }
            catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or EndOfStreamException)
            {
                return;
            }

            foreach (var export in package.Exports.Where(export => export.OuterIndex == 0))
            {
                topLevel.GetOrAdd(export.ObjectName, _ => []).Add(path);
            }

            visit?.Invoke(package);
        });

        lock (_indexLock)
        {
            _topLevelObjects = topLevel.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The object a package index refers to: an export of the package itself, or an import found in
    /// whichever package defines it. Null when it is None or no package defines it.
    /// </summary>
    public (UE3Package Package, UE3PackageExport Export)? Resolve(UE3Package package, int packageIndex)
    {
        if (package.GetExport(packageIndex) is { } export)
        {
            return (package, export);
        }

        if (package.GetImport(packageIndex) is not { } import)
        {
            return null;
        }

        // Drop the package name: cooked files hold their objects at the top level.
        var segments = package.GetPath(packageIndex).Split('.');
        if (segments.Length < 2)
        {
            return null;
        }

        var objectPath = string.Join('.', segments[1..]);
        foreach (var candidatePath in FindDefiningPackages(segments[1]))
        {
            UE3Package candidate;
            try
            {
                candidate = GetPackage(candidatePath);
            }
            catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or EndOfStreamException)
            {
                continue;
            }

            for (var index = 1; index <= candidate.Exports.Count; index++)
            {
                var candidateExport = candidate.Exports[index - 1];
                if (candidateExport.ObjectName.Equals(import.ObjectName, StringComparison.OrdinalIgnoreCase) &&
                    candidate.GetClassName(candidateExport).Equals(import.ClassName, StringComparison.OrdinalIgnoreCase) &&
                    candidate.GetPath(index).Equals(objectPath, StringComparison.OrdinalIgnoreCase))
                {
                    return (candidate, candidateExport);
                }
            }
        }

        return null;
    }

    /// <summary>Every top-level object with this name and class, in whichever packages define it.</summary>
    public IEnumerable<(UE3Package Package, UE3PackageExport Export)> FindTopLevel(string objectName, string className)
    {
        foreach (var path in FindDefiningPackages(objectName))
        {
            UE3Package package;
            try
            {
                package = GetPackage(path);
            }
            catch (Exception ex) when (ex is GMConverterException or IOException or UnauthorizedAccessException or EndOfStreamException)
            {
                continue;
            }

            if (package.Exports.FirstOrDefault(export => export.OuterIndex == 0 &&
                    export.ObjectName.Equals(objectName, StringComparison.OrdinalIgnoreCase) &&
                    package.GetClassName(export).Equals(className, StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                yield return (package, found);
            }
        }
    }

    private List<string> FindDefiningPackages(string topLevelName)
    {
        Dictionary<string, List<string>>? index;
        lock (_indexLock)
        {
            index = _topLevelObjects;
        }

        if (index is null)
        {
            // Resolving before a scan: index on demand.
            Scan();
            lock (_indexLock)
            {
                index = _topLevelObjects!;
            }
        }

        return index.TryGetValue(topLevelName, out var paths) ? paths : [];
    }

    private static FileKind Classify(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[8];
            if (stream.Read(header) < header.Length || BinaryPrimitives.ReadUInt32LittleEndian(header) != UE3Compression.PackageTag)
            {
                return FileKind.Other;
            }

            var version = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
            if (UE3Package.IsPackageVersion(version))
            {
                return FileKind.Package;
            }

            // A texture cache starts straight away with a compressed chunk: the tag, then a block size.
            var blockSize = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            return blockSize is > 0 and <= 0x4000000 && (blockSize & (blockSize - 1)) == 0 ? FileKind.Cache : FileKind.Other;
        }
        catch (IOException)
        {
            return FileKind.Other;
        }
        catch (UnauthorizedAccessException)
        {
            return FileKind.Other;
        }
    }

    private enum FileKind
    {
        Other,
        Package,
        Cache
    }
}
