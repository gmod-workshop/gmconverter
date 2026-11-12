using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AssetsTools.NET.Extra;
using GMConverter.SDK.Common;
using GMConverter.SDK.Explorer;
using GMConverter.Unity.Assets;
using GMConverter.Unity.Audio;
using GMConverter.Unity.Importers;

namespace GMConverter.Unity.Explorer;

/// <summary>
/// Browses a Unity player build. Lists prefabs and scene objects that hold renderers (each becomes
/// a model with everything under it), every mesh on its own, with the materials of a renderer
/// that draws it, every animation clip, which can be added to a model, and every audio clip as a
/// sound.
/// </summary>
public sealed class UnityExplorer : IExplorer
{
    private const string _referenceFolder = "__unity";

    // Audio clips resolve to their FMOD sound bank, which FsbImporter reads.
    private const string _soundFormat = "fsb";

    private static readonly HashSet<AssetClassID> _rendererClasses = [AssetClassID.MeshRenderer, AssetClassID.SkinnedMeshRenderer];

    public string Id => "unity";

    public string DisplayName => "Unity player builds";

    public bool Supports(ExplorerTarget target)
    {
        return UnityGameFolder.FindDataDirectory(target.FullPath) is not null;
    }

    public IReadOnlyList<ExplorerFileEntry> Scan(ExplorerTarget target)
    {
        var dataDirectory = UnityGameFolder.FindDataDirectory(target.FullPath)
            ?? throw new GMConverterException($"Not a Unity player build: {target.FullPath}");
        var library = UnityAssetLibrary.For(dataDirectory);

        lock (library.SyncRoot)
        {
            var sceneNames = ReadSceneNames(library);
            var fileNames = UnityGameFolder.GetAssetFileNames(dataDirectory);
            List<ExplorerFileEntry> entries = [];
            Dictionary<UnityObjectKey, UnityObjectKey> meshRenderers = [];

            foreach (var fileName in fileNames)
            {
                var file = library.Open(fileName);
                var folder = UnityGameFolder.GetSceneIndex(fileName) is { } sceneIndex
                    ? $"Scenes/{sceneNames.GetValueOrDefault(sceneIndex, fileName)}"
                    : "Prefabs";

                foreach (var transform in library.ObjectsOfType(file, AssetClassID.Transform))
                {
                    if (!UnityFields.IsNullPointer(transform["m_Father"]))
                    {
                        continue;
                    }

                    var (rigid, skinned) = CountRenderers(library, transform);
                    if (rigid + skinned == 0)
                    {
                        continue;
                    }

                    var name = library.Resolve(transform.File, transform["m_GameObject"])?.Name ?? "GameObject";
                    var details = string.Create(CultureInfo.InvariantCulture,
                        $"GameObject {name} | {fileName} | Path ID {transform.PathId} | {skinned} skinned, {rigid} static renderers");
                    entries.Add(CreateEntry(dataDirectory, $"{folder}/{Sanitize(name)}", fileName, transform.Key.ToString(), "GameObject", details));
                }

                IndexMeshRenderers(library, file, meshRenderers);
            }

            foreach (var fileName in fileNames)
            {
                var file = library.Open(fileName);
                foreach (var info in file.file.GetAssetsOfType(AssetClassID.Mesh))
                {
                    var key = new UnityObjectKey(fileName, info.PathId);
                    var name = library.GetNameFast(file, info);
                    var entryPath = meshRenderers.TryGetValue(key, out var renderer) ? $"{key}|{renderer}" : key.ToString();
                    var details = string.Create(CultureInfo.InvariantCulture, $"Mesh {name} | {fileName} | Path ID {info.PathId}");
                    entries.Add(CreateEntry(dataDirectory, $"Meshes/{Path.GetFileNameWithoutExtension(fileName)}/{Sanitize(name)}", fileName, entryPath, "Mesh", details));
                }

                foreach (var info in file.file.GetAssetsOfType(AssetClassID.AnimationClip))
                {
                    var key = new UnityObjectKey(fileName, info.PathId);
                    var name = library.GetNameFast(file, info);
                    var details = string.Create(CultureInfo.InvariantCulture, $"AnimationClip {name} | {fileName} | Path ID {info.PathId}");
                    entries.Add(CreateEntry(dataDirectory, $"Animations/{Path.GetFileNameWithoutExtension(fileName)}/{Sanitize(name)}", fileName, key.ToString(), "AnimationClip", details) with
                    {
                        IsConvertible = false,
                        Kind = ExplorerEntryKind.Animation,
                        CanAddToModel = true,
                    });
                }

                foreach (var clip in library.ObjectsOfType(file, AssetClassID.AudioClip))
                {
                    var audio = new UnityAudioClip(clip);
                    var details = string.Create(CultureInfo.InvariantCulture, $"AudioClip {audio.Name} | {fileName} | Path ID {clip.PathId} | {audio.Describe()}");
                    entries.Add(CreateEntry(dataDirectory, $"Sounds/{Path.GetFileNameWithoutExtension(fileName)}/{Sanitize(audio.Name)}", fileName, clip.Key.ToString(), "AudioClip", details) with
                    {
                        InputFormat = _soundFormat,
                        Kind = ExplorerEntryKind.Sound,
                    });
                }
            }

            return MakeDisplayPathsUnique(entries)
                .OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public ExplorerResolvedEntry ResolveEntry(ExplorerFileEntry fileEntry)
    {
        if (fileEntry.ArchivePath is not { } dataDirectory || fileEntry.ArchiveEntryPath is not { } entryPath)
        {
            throw new GMConverterException("Unity entry is missing its object reference.");
        }

        var parts = entryPath.Split('|');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entryPath)))[..12];
        var name = Sanitize(Path.GetFileName(fileEntry.DisplayPath));
        var directory = Path.Join(ExplorerFileSystem.GetExtractionRoot(dataDirectory), _referenceFolder, hash);
        Directory.CreateDirectory(directory);

        if (fileEntry.Kind == ExplorerEntryKind.Sound)
        {
            var bankPath = Path.Join(directory, name + UnityAudioClip.BankExtension);
            var library = UnityAssetLibrary.For(dataDirectory);
            lock (library.SyncRoot)
            {
                var clip = new UnityAudioClip(library.Get(UnityObjectKey.Parse(parts[0])));
                File.WriteAllBytes(bankPath, clip.ReadBank(library));
            }

            return new ExplorerResolvedEntry(bankPath, directory, fileEntry.Details);
        }

        var reference = new UnityAssetReference(dataDirectory, parts[0], parts.Length > 1 ? parts[1] : null);
        var path = Path.Join(directory, name + UnityAssetReference.FileExtension);
        reference.Write(path);

        // A clip is added to the model being converted rather than converted itself.
        return fileEntry.Kind == ExplorerEntryKind.Animation
            ? new ExplorerResolvedEntry(path, directory, fileEntry.Details, new Dictionary<string, object?> { [UnityImporter.Animations] = parts[0] })
            : new ExplorerResolvedEntry(path, directory, fileEntry.Details);
    }

    public void ClearCaches()
    {
        UnityAssetLibrary.ClearAll();
    }

    private ExplorerFileEntry CreateEntry(string dataDirectory, string displayPath, string fileName, string entryPath, string assetClass, string details)
    {
        return new ExplorerFileEntry(
            displayPath,
            Path.Join(dataDirectory, fileName),
            "unity",
            dataDirectory,
            dataDirectory,
            dataDirectory,
            entryPath,
            Id,
            details,
            AssetClass: assetClass);
    }

    // Scene N of the build settings is saved as levelN.
    private static Dictionary<int, string> ReadSceneNames(UnityAssetLibrary library)
    {
        var gameManagers = library.Open(UnityGameFolder.GameManagersFileName);
        var buildSettings = library.ObjectsOfType(gameManagers, AssetClassID.BuildSettings).FirstOrDefault();
        if (buildSettings is null)
        {
            return [];
        }

        return UnityFields.Items(buildSettings["scenes"])
            .Select((scene, index) => (index, name: Path.GetFileNameWithoutExtension(scene.AsString)))
            .ToDictionary(scene => scene.index, scene => Sanitize(scene.name));
    }

    private static (int Rigid, int Skinned) CountRenderers(UnityAssetLibrary library, UnityObject root)
    {
        var rigid = 0;
        var skinned = 0;
        Stack<UnityObject> pending = new([root]);

        while (pending.Count > 0)
        {
            var transform = pending.Pop();
            if (library.Resolve(transform.File, transform["m_GameObject"]) is { } gameObject)
            {
                foreach (var component in UnityFields.Items(gameObject["m_Component"]))
                {
                    var classId = library.PeekClass(gameObject.File, component["component"]);
                    rigid += classId == AssetClassID.MeshRenderer ? 1 : 0;
                    skinned += classId == AssetClassID.SkinnedMeshRenderer ? 1 : 0;
                }
            }

            foreach (var child in UnityFields.Items(transform["m_Children"]))
            {
                if (library.Resolve(transform.File, child) is { } childTransform)
                {
                    pending.Push(childTransform);
                }
            }
        }

        return (rigid, skinned);
    }

    // Remembers one renderer drawing each mesh, so a mesh listed on its own keeps its materials.
    private static void IndexMeshRenderers(UnityAssetLibrary library, AssetsFileInstance file, Dictionary<UnityObjectKey, UnityObjectKey> meshRenderers)
    {
        foreach (var filter in library.ObjectsOfType(file, AssetClassID.MeshFilter))
        {
            if (library.ResolveKey(file, filter["m_Mesh"]) is not { } mesh ||
                meshRenderers.ContainsKey(mesh) ||
                library.Resolve(file, filter["m_GameObject"]) is not { } gameObject)
            {
                continue;
            }

            foreach (var component in UnityFields.Items(gameObject["m_Component"]))
            {
                if (library.PeekClass(gameObject.File, component["component"]) is { } classId && _rendererClasses.Contains(classId) &&
                    library.ResolveKey(gameObject.File, component["component"]) is { } renderer)
                {
                    meshRenderers[mesh] = renderer;
                    break;
                }
            }
        }
    }

    private static IEnumerable<ExplorerFileEntry> MakeDisplayPathsUnique(List<ExplorerFileEntry> entries)
    {
        var duplicates = entries
            .GroupBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return entries.Select(entry => duplicates.Contains(entry.DisplayPath)
            ? entry with { DisplayPath = $"{entry.DisplayPath}_{ObjectPathId(entry)}" }
            : entry);
    }

    private static string ObjectPathId(ExplorerFileEntry entry)
    {
        var key = UnityObjectKey.Parse(entry.ArchiveEntryPath!.Split('|')[0]);
        return key.PathId.ToString(CultureInfo.InvariantCulture);
    }

    private static string Sanitize(string value)
    {
        var sanitized = string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_')).Trim('_', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? "Object" : sanitized;
    }
}
