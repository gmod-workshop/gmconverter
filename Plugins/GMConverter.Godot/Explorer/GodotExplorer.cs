using System.Security.Cryptography;
using System.Text;
using GMConverter.Godot.Audio;
using GMConverter.Godot.Packs;
using GMConverter.SDK.Common;
using GMConverter.SDK.Explorer;

namespace GMConverter.Godot.Explorer;

/// <summary>
/// Browses a Godot 4 game's resource pack, listing assets under the paths they had in the
/// project: every model the project imported (<c>.glb</c>, <c>.fbx</c> and other scenes), the
/// game's own scenes, which put models together, and every sound it imported.
/// </summary>
public sealed class GodotExplorer : IExplorer
{
    private const string _extractionFolder = "__godot";
    private const string _soundFormat = "godotaudio";
    private const string _modelFormat = "godot";
    private const string _sceneImporter = "scene";

    private static readonly Dictionary<string, string> _soundImporters = new(StringComparer.Ordinal)
    {
        ["oggvorbisstr"] = "Ogg Vorbis",
        ["wav"] = "WAV",
    };

    public string Id => "godot";

    public string DisplayName => "Godot games";

    public bool Supports(ExplorerTarget target)
    {
        return GodotGameFolder.FindPack(target.FullPath) is not null;
    }

    public IReadOnlyList<ExplorerFileEntry> Scan(ExplorerTarget target)
    {
        var packPath = GodotGameFolder.FindPack(target.FullPath)
            ?? throw new GMConverterException($"Not a Godot game: {target.FullPath}");
        var pack = GodotPack.For(packPath);
        List<ExplorerFileEntry> entries = [];

        foreach (var remap in pack.Remaps)
        {
            if (remap.TargetPath is { } scenePath && pack.Find(scenePath) is not null && IsScene(remap))
            {
                var imported = remap.Importer == _sceneImporter;
                var folder = imported ? "Models" : "Scenes";
                var sceneDetails = $"{(imported ? "Imported model" : "Scene")} | {remap.SourcePath} | Godot {pack.EngineVersion}";
                entries.Add(new ExplorerFileEntry(
                    $"{folder}/{DisplayPath(remap.SourcePath)}",
                    packPath,
                    _modelFormat,
                    Path.GetDirectoryName(packPath) ?? packPath,
                    packPath,
                    packPath,
                    remap.SourcePath,
                    Id,
                    sceneDetails,
                    AssetClass: imported ? "Model" : "PackedScene"));
                continue;
            }

            if (!_soundImporters.TryGetValue(remap.Importer, out var kind) ||
                remap.TargetPath is not { } targetPath ||
                pack.Find(targetPath) is null)
            {
                continue;
            }

            var displayPath = "Sounds/" + DisplayPath(remap.SourcePath);
            var details = $"{kind} | {remap.SourcePath} | Godot {pack.EngineVersion}";
            entries.Add(new ExplorerFileEntry(
                displayPath,
                packPath,
                _soundFormat,
                Path.GetDirectoryName(packPath) ?? packPath,
                packPath,
                packPath,
                targetPath,
                Id,
                details,
                AssetClass: kind,
                Kind: ExplorerEntryKind.Sound));
        }

        return [.. entries.OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)];
    }

    public ExplorerResolvedEntry ResolveEntry(ExplorerFileEntry fileEntry)
    {
        if (fileEntry.ArchivePath is not { } packPath || fileEntry.ArchiveEntryPath is not { } targetPath)
        {
            throw new GMConverterException("Godot entry is missing its pack file.");
        }

        var pack = GodotPack.For(packPath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(targetPath)))[..12];
        var directory = Path.Join(ExplorerFileSystem.GetExtractionRoot(packPath), _extractionFolder, hash);
        Directory.CreateDirectory(directory);

        if (fileEntry.Kind == ExplorerEntryKind.Model)
        {
            // Models are read from the pack by the importer; it gets a pointer to the scene.
            var referencePath = Path.Join(directory, Sanitize(Path.GetFileNameWithoutExtension(fileEntry.DisplayPath)) + GodotAssetReference.FileExtension);
            new GodotAssetReference(packPath, targetPath).Write(referencePath);
            return new ExplorerResolvedEntry(referencePath, directory, fileEntry.Details);
        }

        // Named after the source file, with the imported file's extension for the importer to match.
        var name = Path.GetFileNameWithoutExtension(fileEntry.DisplayPath);
        var extension = targetPath.EndsWith(GodotAudio.SampleExtension, StringComparison.Ordinal) ? GodotAudio.SampleExtension : GodotAudio.OggExtension;
        var path = Path.Join(directory, Sanitize(name) + extension);
        File.WriteAllBytes(path, pack.Read(targetPath));
        return new ExplorerResolvedEntry(path, directory, fileEntry.Details);
    }

    public void ClearCaches()
    {
        GodotPack.ClearAll();
    }

    // An imported model, or a scene of the game's own (saved as .tscn or .scn, remapped on export).
    private static bool IsScene(GodotRemap remap)
    {
        return remap.Importer == _sceneImporter ||
            (remap.Importer.Length == 0 && (remap.SourcePath.EndsWith(".tscn", StringComparison.Ordinal) || remap.SourcePath.EndsWith(".scn", StringComparison.Ordinal)));
    }

    // The project path without "res://", folder by folder.
    private static string DisplayPath(string sourcePath)
    {
        var relative = sourcePath.StartsWith("res://", StringComparison.Ordinal) ? sourcePath["res://".Length..] : sourcePath;
        return string.Join('/', relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Sanitize));
    }

    private static string Sanitize(string value)
    {
        var sanitized = string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_')).Trim('_', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? "File" : sanitized;
    }
}
