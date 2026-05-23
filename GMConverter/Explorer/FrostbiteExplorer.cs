using System.Text.Json;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using GMConverter.Common;
using GMConverter.Importers;

namespace GMConverter.Explorer;

// First-cut Frostbite explorer profile. Detects a Frostbite install root (the directory
// containing both the game .exe and the Data/ subfolder), mounts it through FrostySdk's
// global managers via FrostbiteContext, and lists static-mesh + texture EBX entries.
// Resolution (asset extraction to a PSK or texture file) is wired up in a later task; until
// then entries are returned with IsConvertible = false so the UI surfaces them as browse-only.
internal sealed class FrostbiteExplorer : IExplorer
{
    // Frostbite EBX type-names we care about for MVP. SkinnedMeshAsset is deliberately excluded
    // — skeletal mesh support is phase 2 (the parser has additional bone palette + skinning
    // stream work). TextureAsset is included for browse-only display so users can confirm what's
    // present even before the texture exporter lands.
    private static readonly HashSet<string> _surfacedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MeshAsset",
        "RigidMeshAsset",
        "CompositeMeshAsset",
        "TextureAsset"
    };

    // Mesh classes that can be resolved through FrostbiteImporter. CompositeMeshAsset works as
    // long as its parts are baked into local space, which is the common case for environment
    // props in Squadrons; assets that need per-part transforms applied will look misaligned
    // until that path is wired (see MeshSetReader for the data layout).
    private static readonly HashSet<string> _resolvableMeshTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MeshAsset",
        "RigidMeshAsset",
        "CompositeMeshAsset"
    };

    private static readonly JsonSerializerOptions _manifestJsonOptions = new() { WriteIndented = true };

    public string Id => "frostbite";

    public string DisplayName => "Frostbite (Star Wars) archives";

    public bool Supports(ExplorerTarget target)
    {
        return target.IsDirectory && FrostbiteContext.LooksLikeFrostbiteRoot(target.FullPath);
    }

    public IReadOnlyList<ExplorerFileEntry> Scan(ExplorerTarget target)
    {
        using var scanScope = PerfTimer.Measure("frostbite.scan", "Scan", target.FullPath);

        var mount = FrostbiteContext.Mount(target.FullPath);

        var entries = new List<ExplorerFileEntry>();
        var typeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var totalScanned = 0;

        foreach (var entry in AssetManager.EnumerateEbxAssetEntries())
        {
            totalScanned++;
            if (!_surfacedTypes.Contains(entry.Type))
            {
                continue;
            }

            typeCounts[entry.Type] = typeCounts.GetValueOrDefault(entry.Type) + 1;
            entries.Add(BuildFileEntry(entry, mount.InstallRoot));
        }

        PerfTimer.Log("frostbite.scan",
            $"surfaced={entries.Count}/{totalScanned} " +
            $"counts={{ {string.Join(", ", typeCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"))} }}");

        return entries;
    }

    public ExplorerResolvedEntry ResolveEntry(ExplorerFileEntry fileEntry)
    {
        if (string.IsNullOrEmpty(fileEntry.ArchiveEntryPath) || string.IsNullOrEmpty(fileEntry.ArchivePath))
        {
            throw new GMConverterException("Frostbite entry is missing archive metadata; cannot resolve.");
        }

        if (!_resolvableMeshTypes.Contains(fileEntry.AssetClass ?? string.Empty))
        {
            throw new GMConverterException(
                $"Frostbite asset class \"{fileEntry.AssetClass}\" is not resolvable yet. " +
                $"Supported classes: {string.Join(", ", _resolvableMeshTypes)}.");
        }

        // .frostbiteref is a tiny JSON pointer the FrostbiteImporter reads back. We park each one
        // in a per-resolve directory under temp so the existing UI cleanup (which deletes the
        // resolved-input directory after preview/conversion) sweeps it without extra wiring.
        var resolveDir = Path.Combine(Path.GetTempPath(), "GMConverter", "FrostbiteResolved", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resolveDir);

        var safeName = NameHelpers.SanitizeFileName(Path.GetFileName(fileEntry.ArchiveEntryPath));
        var manifestPath = Path.Combine(resolveDir, $"{safeName}.frostbiteref");
        var manifest = new FrostbiteImporter.FrostbiteRefManifest(fileEntry.ArchivePath, fileEntry.ArchiveEntryPath);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, _manifestJsonOptions));

        return new ExplorerResolvedEntry(
            InputPath: manifestPath,
            MaterialDirectory: fileEntry.MaterialDirectory,
            Details: $"Frostbite {fileEntry.AssetClass} → manifest at {manifestPath}");
    }

    public void ClearCaches()
    {
        // FrostySdk's static singletons don't currently expose a clean teardown path, so cache
        // clears are a no-op at this layer. Restarting the application is the only way to switch
        // installs; this is surfaced explicitly when Mount() is called with a different root.
    }

    private ExplorerFileEntry BuildFileEntry(EbxAssetEntry entry, string installRoot)
    {
        // The Type field is populated by FrostySdk during AssetManager init via
        // BaseEbxReader.GetRootType() — does not require TypeLibrary, so we get it for free
        // even though we deliberately skipped TypeLibrary.Initialize() in FrostbiteContext.
        var displayPath = $"{entry.Path}/{entry.Type}/{entry.Filename}";
        var sizeMb = entry.OriginalSize / (1024.0 * 1024.0);
        var details = sizeMb >= 0.01
            ? $"Class {entry.Type} | Guid {entry.Guid} | Size {sizeMb:0.00} MB"
            : $"Class {entry.Type} | Guid {entry.Guid}";

        // InputFormat selects the importer post-resolve. For resolvable mesh classes we point at
        // the new "frostbite" importer; everything else stays browse-only with a descriptive
        // badge ("texture", "compositemesh") that will swap to "frostbite" when those paths land.
        var isResolvable = _resolvableMeshTypes.Contains(entry.Type);
        string inputFormat;
        if (isResolvable)
        {
            inputFormat = "frostbite";
        }
        else if (string.Equals(entry.Type, "TextureAsset", StringComparison.OrdinalIgnoreCase))
        {
            inputFormat = "texture";
        }
        else
        {
            inputFormat = "mesh";
        }

        return new ExplorerFileEntry(
            DisplayPath: displayPath,
            FilePath: entry.Name,
            InputFormat: inputFormat,
            MaterialDirectory: installRoot,
            SearchRoot: installRoot,
            ArchivePath: installRoot,
            ArchiveEntryPath: entry.Name,
            ExplorerId: Id,
            Details: details,
            IsConvertible: isResolvable,
            AssetClass: entry.Type);
    }
}
