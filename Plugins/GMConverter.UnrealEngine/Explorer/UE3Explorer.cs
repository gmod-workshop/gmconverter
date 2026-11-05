using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using GMConverter.SDK.Common;
using GMConverter.SDK.Explorer;
using GMConverter.UnrealEngine.Formats.UE3.Exporters;
using GMConverter.UnrealEngine.Formats.UE3.Packages;
using GMConverter.UnrealEngine.Importers;

namespace GMConverter.UnrealEngine.Explorer;

/// <summary>
/// Browses an Unreal Engine 3 game's cooked packages for skeletal meshes and anim sets. Meshes
/// export with their materials baked and the anim sets of their own package that fit them; anim
/// sets can be added to any model.
/// </summary>
public sealed class UE3Explorer : IExplorer
{
    private const int _supportProbeLimit = 4000;

    private readonly ConcurrentDictionary<string, UE3GameContent> _contents = new(StringComparer.OrdinalIgnoreCase);

    public string Id => "ue3";

    public string DisplayName => "Unreal Engine 3 packages";

    public bool Supports(ExplorerTarget target)
    {
        if (target.IsFile)
        {
            return UE3Package.IsPackage(target.FullPath);
        }

        return target.IsDirectory && ExplorerFileSystem.EnumerateFiles(target.FullPath)
            .Take(_supportProbeLimit)
            .Any(UE3Package.IsPackage);
    }

    public IReadOnlyList<ExplorerFileEntry> Scan(ExplorerTarget target)
    {
        var root = target.IsFile ? Path.GetDirectoryName(target.FullPath) ?? target.FullPath : target.FullPath;
        var content = GetContent(root);
        ConcurrentBag<(UE3Package Package, int Index, string ClassName)> assets = [];
        ConcurrentDictionary<(string Object, string Class, string Package), int> packageVotes = new();
        content.Scan(package =>
        {
            for (var index = 1; index <= package.Exports.Count; index++)
            {
                var export = package.Exports[index - 1];
                var className = package.GetClassName(export);
                if (export.SerialSize > 0 && className is "SkeletalMesh" or "AnimSet")
                {
                    assets.Add((package, index, className));
                }
            }

            // Packages aren't named in their own files; other packages' imports name them.
            for (var index = 1; index <= package.Imports.Count; index++)
            {
                var import = package.Imports[index - 1];
                if (package.GetImport(import.OuterIndex) is { OuterIndex: 0, ClassName: "Package" } outer)
                {
                    packageVotes.AddOrUpdate((import.ObjectName, import.ClassName, outer.ObjectName), 1, (_, count) => count + 1);
                }
            }
        });

        var packageNames = NamePackages(assets.Select(asset => asset.Package).Distinct(), packageVotes);
        var targetFile = target.IsFile ? Path.GetFullPath(target.FullPath) : null;
        return [.. assets
            .Where(asset => targetFile is null || Path.GetFullPath(asset.Package.FilePath).Equals(targetFile, StringComparison.OrdinalIgnoreCase))
            .Select(asset => CreateEntry(asset.Package, asset.Index, asset.ClassName, packageNames[asset.Package.FilePath], root))
            .OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)];
    }

    public ExplorerResolvedEntry ResolveEntry(ExplorerFileEntry fileEntry)
    {
        if (fileEntry.ArchivePath is null || fileEntry.ArchiveEntryPath is null || fileEntry.AssetClass is null)
        {
            throw new GMConverterException("UE3 package entry is missing export metadata.");
        }

        var content = GetContent(fileEntry.SearchRoot);
        var package = content.GetPackage(fileEntry.ArchivePath);
        var exportIndex = Enumerable.Range(1, package.Exports.Count).FirstOrDefault(index =>
            package.GetClassName(package.Exports[index - 1]).Equals(fileEntry.AssetClass, StringComparison.OrdinalIgnoreCase) &&
            package.GetPath(index).Equals(fileEntry.ArchiveEntryPath, StringComparison.OrdinalIgnoreCase));
        if (exportIndex == 0)
        {
            throw new GMConverterException($"UE3 export not found: {fileEntry.ArchiveEntryPath}");
        }

        var export = package.Exports[exportIndex - 1];
        var outputDirectory = GetExportRoot(fileEntry);
        if (fileEntry.AssetClass.Equals("AnimSet", StringComparison.OrdinalIgnoreCase))
        {
            var animation = UE3MeshExporter.ExportAnimSet(package, export, outputDirectory);
            return new ExplorerResolvedEntry(animation.AnimationPaths[0], outputDirectory, ImporterOptions: PSKImporterOptions.WithAnimation(animation.AnimationPaths[0]));
        }

        var result = UE3MeshExporter.ExportMesh(content, package, export, outputDirectory);
        return new ExplorerResolvedEntry(
            result.MeshPath!,
            outputDirectory,
            ImporterOptions: PSKImporterOptions.WithAnimation(result.AnimationPaths.Count == 0
                ? null
                : string.Join(SDK.Options.OptionDescriptor.MultipleValueSeparator, result.AnimationPaths)));
    }

    public void ClearCaches()
    {
        _contents.Clear();
    }

    private UE3GameContent GetContent(string root)
    {
        return _contents.GetOrAdd(Path.GetFullPath(root), UE3GameContent.Open);
    }

    // Each file takes the package name that the most of its objects (by name and class) are
    // imported under elsewhere, or its file name when nothing imports from it.
    private static Dictionary<string, string> NamePackages(
        IEnumerable<UE3Package> packages,
        ConcurrentDictionary<(string Object, string Class, string Package), int> votes)
    {
        var byObject = votes
            .GroupBy(vote => (vote.Key.Object.ToUpperInvariant(), vote.Key.Class.ToUpperInvariant()))
            .ToDictionary(group => group.Key, group => group.Select(vote => (vote.Key.Package, vote.Value)).ToArray());
        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            var best = package.Exports
                .Where(export => export.OuterIndex == 0)
                .SelectMany(export => byObject.TryGetValue(
                    (export.ObjectName.ToUpperInvariant(), package.GetClassName(export).ToUpperInvariant()),
                    out var candidates) ? candidates.Select(candidate => (Export: export.ObjectName, candidate.Package, candidate.Value)) : [])
                .GroupBy(vote => vote.Package, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Select(vote => vote.Export).Distinct(StringComparer.OrdinalIgnoreCase).Count())
                .ThenByDescending(group => group.Sum(vote => vote.Value))
                .FirstOrDefault()?.Key;
            names[package.FilePath] = best ?? Path.GetFileNameWithoutExtension(package.FilePath);
        }

        return names;
    }

    private ExplorerFileEntry CreateEntry(UE3Package package, int exportIndex, string className, string packageName, string root)
    {
        var export = package.Exports[exportIndex - 1];
        var objectPath = package.GetPath(exportIndex);
        var isAnimation = className == "AnimSet";
        return new ExplorerFileEntry(
            $"{Sanitize(packageName)}/{className}/{string.Join('/', objectPath.Split('.').Select(Sanitize))}",
            package.FilePath,
            "psk",
            root,
            root,
            package.FilePath,
            objectPath,
            Id,
            $"Class {className} | Object {objectPath} | Package {packageName} | File {Path.GetFileName(package.FilePath)} | Version {package.Summary.FileVersion}/{package.Summary.LicenseeVersion} | Size {export.SerialSize:N0}",
            AssetClass: className,
            Kind: isAnimation ? ExplorerEntryKind.Animation : ExplorerEntryKind.Model,
            CanAddToModel: isAnimation);
    }

    private static string GetExportRoot(ExplorerFileEntry fileEntry)
    {
        var archivePath = fileEntry.ArchivePath ?? fileEntry.FilePath;
        var key = $"{Path.GetFullPath(archivePath)}|{fileEntry.AssetClass}|{fileEntry.ArchiveEntryPath}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12];
        return Path.Combine(ExplorerFileSystem.GetExtractionRoot(archivePath), "__ue3_exports", hash);
    }

    private static string Sanitize(string value)
    {
        var sanitized = string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_')).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "Object" : sanitized;
    }
}
