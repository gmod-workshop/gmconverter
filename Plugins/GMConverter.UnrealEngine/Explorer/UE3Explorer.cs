using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using GMConverter.SDK.Common;
using GMConverter.SDK.Explorer;
using GMConverter.UnrealEngine.Formats.UE3.Exporters;
using GMConverter.UnrealEngine.Formats.UE3.Packages;
using GMConverter.UnrealEngine.Importers;
using GMConverter.UnrealEngine.Profiles.CrossFireHD;

namespace GMConverter.UnrealEngine.Explorer;

/// <summary>
/// Browses an Unreal Engine 3 game's cooked packages for skeletal meshes and anim sets. Meshes
/// export with their materials baked and the anim sets of their own package that fit them; anim
/// sets can be added to any model. Weapons with first-person arm animations also get a
/// <c>Viewmodel</c> entry that assembles the arms, the weapon and their clips into one model.
/// Sound cues export as sounds, one sound event per layer of the cue.
/// </summary>
public sealed class UE3Explorer : IExplorer
{
    private const int _supportProbeLimit = 4000;
    private const string _viewmodelClass = "Viewmodel";

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
                if (export.SerialSize > 0 && className is "SkeletalMesh" or "AnimSet" or "SoundCue")
                {
                    assets.Add((package, index, className));
                }
            }

            // A package holding a weapon and its first-person arm animations can build a view model.
            if (package.Exports.Any(export => package.GetClassName(export) == "AnimSet" &&
                    CrossFireHDViewmodels.IsViewmodelSet(export.ObjectName, CrossFireHDViewmodels.ArmsSetPrefix)))
            {
                for (var index = 1; index <= package.Exports.Count; index++)
                {
                    var export = package.Exports[index - 1];
                    if (export.SerialSize > 0 && package.GetClassName(export) == "SkeletalMesh" && CrossFireHDViewmodels.IsWeaponMesh(export.ObjectName))
                    {
                        assets.Add((package, index, _viewmodelClass));
                    }
                }
            }

            // Packages aren't named in their own files; other packages' imports name them, by each
            // object's path below the package (groups such as "Cue" alone are shared by many packages).
            for (var index = 1; index <= package.Imports.Count; index++)
            {
                var import = package.Imports[index - 1];
                if (import.ClassName != "Package" && ImportedFrom(package, import) is { } imported)
                {
                    packageVotes.AddOrUpdate((imported.Path, import.ClassName, imported.Package), 1, (_, count) => count + 1);
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
        var exportClass = fileEntry.AssetClass.Equals(_viewmodelClass, StringComparison.OrdinalIgnoreCase) ? "SkeletalMesh" : fileEntry.AssetClass;
        var exportIndex = Enumerable.Range(1, package.Exports.Count).FirstOrDefault(index =>
            package.GetClassName(package.Exports[index - 1]).Equals(exportClass, StringComparison.OrdinalIgnoreCase) &&
            package.GetPath(index).Equals(fileEntry.ArchiveEntryPath, StringComparison.OrdinalIgnoreCase));
        if (exportIndex == 0)
        {
            throw new GMConverterException($"UE3 export not found: {fileEntry.ArchiveEntryPath}");
        }

        var export = package.Exports[exportIndex - 1];
        var outputDirectory = GetExportRoot(fileEntry);
        if (fileEntry.AssetClass.Equals("SoundCue", StringComparison.OrdinalIgnoreCase))
        {
            return new ExplorerResolvedEntry(UE3SoundExporter.Export(content, package, export, outputDirectory), outputDirectory);
        }

        if (fileEntry.AssetClass.Equals(_viewmodelClass, StringComparison.OrdinalIgnoreCase))
        {
            var viewmodel = UE3ViewmodelExporter.Export(content, package, export, outputDirectory);
            return new ExplorerResolvedEntry(viewmodel.MeshPath!, outputDirectory, ImporterOptions: PSKImporterOptions.WithAnimation(viewmodel.AnimationPaths[0]));
        }

        if (fileEntry.AssetClass.Equals("AnimSet", StringComparison.OrdinalIgnoreCase))
        {
            var animation = UE3MeshExporter.ExportAnimSet(content, package, export, outputDirectory);
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
                .Select((export, index) => (Path: package.GetPath(index + 1), Class: package.GetClassName(export)))
                .SelectMany(export => byObject.TryGetValue(
                    (export.Path.ToUpperInvariant(), export.Class.ToUpperInvariant()),
                    out var candidates) ? candidates.Select(candidate => (Export: export.Path, candidate.Package, candidate.Value)) : [])
                .GroupBy(vote => vote.Package, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Select(vote => vote.Export).Distinct(StringComparer.OrdinalIgnoreCase).Count())
                .ThenByDescending(group => group.Sum(vote => vote.Value))
                .FirstOrDefault()?.Key;
            names[package.FilePath] = best ?? Path.GetFileNameWithoutExtension(package.FilePath);
        }

        return names;
    }

    // An import's top-level package and its dotted path below that package, or null when it isn't in one.
    private static (string Package, string Path)? ImportedFrom(UE3Package package, UE3PackageImport import)
    {
        List<string> path = [import.ObjectName];
        var outer = package.GetImport(import.OuterIndex);
        for (var depth = 0; outer is not null && depth < 64; depth++)
        {
            if (outer.OuterIndex == 0)
            {
                return outer.ClassName == "Package" ? (outer.ObjectName, string.Join('.', Enumerable.Reverse(path))) : null;
            }

            path.Add(outer.ObjectName);
            outer = package.GetImport(outer.OuterIndex);
        }

        return null;
    }

    private ExplorerFileEntry CreateEntry(UE3Package package, int exportIndex, string className, string packageName, string root)
    {
        var export = package.Exports[exportIndex - 1];
        var objectPath = package.GetPath(exportIndex);
        var isAnimation = className == "AnimSet";
        var isSound = className == "SoundCue";
        return new ExplorerFileEntry(
            $"{Sanitize(packageName)}/{className}/{string.Join('/', objectPath.Split('.').Select(Sanitize))}",
            package.FilePath,
            isSound ? UE3SoundCueImporter.FormatId : "psk",
            root,
            root,
            package.FilePath,
            objectPath,
            Id,
            $"Class {className} | Object {objectPath} | Package {packageName} | File {Path.GetFileName(package.FilePath)} | Version {package.Summary.FileVersion}/{package.Summary.LicenseeVersion} | Size {export.SerialSize:N0}",
            AssetClass: className,
            Kind: isSound ? ExplorerEntryKind.Sound : isAnimation ? ExplorerEntryKind.Animation : ExplorerEntryKind.Model,
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
