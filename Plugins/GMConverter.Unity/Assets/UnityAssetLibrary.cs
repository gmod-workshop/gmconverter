using AssetsTools.NET;
using AssetsTools.NET.Extra;
using GMConverter.SDK.Common;

namespace GMConverter.Unity.Assets;

/// <summary>
/// The serialized files of one Unity player build, opened on demand and kept open. Player builds
/// strip type trees, so objects are read against the embedded class database for the build's
/// Unity version. AssetsTools.NET is not thread-safe: hold <see cref="SyncRoot"/> while reading.
/// </summary>
internal sealed class UnityAssetLibrary
{
    private const string _classPackageResource = "GMConverter.Unity.classdata.tpk";

    private static readonly Dictionary<string, UnityAssetLibrary> _libraries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock _librariesLock = new();

    private readonly AssetsManager _manager = new();
    private readonly Dictionary<string, AssetsFileInstance> _files = new(StringComparer.OrdinalIgnoreCase);
    private bool _classDatabaseLoaded;

    private UnityAssetLibrary(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        using var classPackage = typeof(UnityAssetLibrary).Assembly.GetManifestResourceStream(_classPackageResource)
            ?? throw new GMConverterException("The Unity class database is missing from the plugin.");
        _manager.LoadClassPackage(classPackage);
    }

    public string DataDirectory { get; }

    public Lock SyncRoot { get; } = new();

    /// <summary>The shared library for <paramref name="dataDirectory"/>.</summary>
    public static UnityAssetLibrary For(string dataDirectory)
    {
        var fullPath = Path.GetFullPath(dataDirectory);
        lock (_librariesLock)
        {
            if (!_libraries.TryGetValue(fullPath, out var library))
            {
                library = new UnityAssetLibrary(fullPath);
                _libraries[fullPath] = library;
            }

            return library;
        }
    }

    /// <summary>Closes every open build so files changed on disk are read again.</summary>
    public static void ClearAll()
    {
        lock (_librariesLock)
        {
            foreach (var library in _libraries.Values)
            {
                lock (library.SyncRoot)
                {
                    library._manager.UnloadAll(true);
                }
            }

            _libraries.Clear();
        }
    }

    /// <summary>Opens the serialized file <paramref name="fileName"/> of this build.</summary>
    public AssetsFileInstance Open(string fileName)
    {
        if (_files.TryGetValue(fileName, out var file))
        {
            return file;
        }

        var path = Path.Join(DataDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new GMConverterException($"Unity asset file not found: {path}");
        }

        file = _manager.LoadAssetsFile(path, false);
        EnsureClassDatabase(file);
        _files[fileName] = file;
        return file;
    }

    /// <summary>Every object of class <paramref name="classId"/> in <paramref name="file"/>.</summary>
    public IEnumerable<UnityObject> ObjectsOfType(AssetsFileInstance file, AssetClassID classId)
    {
        foreach (var info in file.file.GetAssetsOfType(classId))
        {
            yield return Read(file, info);
        }
    }

    /// <summary>The object <paramref name="key"/> names, or null when it does not exist.</summary>
    public UnityObject? Find(UnityObjectKey key)
    {
        var file = Open(key.FileName);
        return file.file.GetAssetInfo(key.PathId) is { } info ? Read(file, info) : null;
    }

    public UnityObject Get(UnityObjectKey key)
    {
        return Find(key) ?? throw new GMConverterException($"Unity object not found: {key}");
    }

    /// <summary>
    /// The object's serialized bytes. Builds without type trees describe only Unity's own classes,
    /// so a script's fields have to be read from these by their known layout.
    /// </summary>
    public static byte[] ReadRawData(UnityObject unityObject)
    {
        var reader = unityObject.File.file.Reader;
        reader.Position = unityObject.Info.GetAbsoluteByteOffset(unityObject.File.file);
        return reader.ReadBytes((int)unityObject.Info.ByteSize);
    }

    /// <summary>Class of the object a PPtr field points at without deserializing it, or null for a null or missing reference.</summary>
    public AssetClassID? PeekClass(AssetsFileInstance file, AssetTypeValueField pointer)
    {
        var target = ResolveFile(file, pointer["m_FileID"].AsInt);
        var pathId = pointer["m_PathID"].AsLong;
        return target is null || pathId == 0 ? null : target.file.GetAssetInfo(pathId) is { } info ? (AssetClassID)info.TypeId : null;
    }

    /// <summary>The key of the object a PPtr field points at without deserializing it, or null for a null or missing reference.</summary>
    public UnityObjectKey? ResolveKey(AssetsFileInstance file, AssetTypeValueField pointer)
    {
        var pathId = pointer["m_PathID"].AsLong;
        return pathId != 0 && ResolveFile(file, pointer["m_FileID"].AsInt) is { } target
            ? new UnityObjectKey(target.name, pathId)
            : null;
    }

    /// <summary>The <c>m_Name</c> of a named object, read without deserializing the rest of it.</summary>
    public string GetNameFast(AssetsFileInstance file, AssetFileInfo info)
    {
        return AssetHelper.GetAssetNameFast(file.file, _manager.ClassDatabase, info);
    }

    /// <summary>The object a PPtr field points at, or null for a null pointer or one into a file this build lacks.</summary>
    public UnityObject? Resolve(AssetsFileInstance file, AssetTypeValueField pointer)
    {
        var pathId = pointer["m_PathID"].AsLong;
        if (pathId == 0)
        {
            return null;
        }

        var target = ResolveFile(file, pointer["m_FileID"].AsInt);
        return target?.file.GetAssetInfo(pathId) is { } info ? Read(target, info) : null;
    }

    /// <summary>
    /// The bytes a mesh or texture keeps outside the serialized file (<c>m_StreamData</c>), or
    /// <paramref name="inlineData"/> when the object stores them inline.
    /// </summary>
    public byte[] ReadStreamedData(AssetTypeValueField streamData, byte[] inlineData)
    {
        if (inlineData.Length > 0 || streamData.IsDummy)
        {
            return inlineData;
        }

        var size = streamData["size"].AsUInt;
        if (size == 0)
        {
            return inlineData;
        }

        return ReadResource(streamData["path"].AsString, (long)streamData["offset"].AsULong, size);
    }

    /// <summary>Reads <paramref name="size"/> bytes at <paramref name="offset"/> of a resource file (<c>.resS</c>, <c>.resource</c>).</summary>
    public byte[] ReadResource(string resourcePath, long offset, long size)
    {
        // Bundled builds write "archive:/CAB-.../CAB-....resS"; loose builds a name next to the file.
        var path = Path.Join(DataDirectory, Path.GetFileName(resourcePath.Replace('\\', '/')));
        if (!File.Exists(path))
        {
            throw new GMConverterException($"Unity resource file not found: {path}");
        }

        using var stream = File.OpenRead(path);
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[size];
        stream.ReadExactly(buffer);
        return buffer;
    }

    private UnityObject Read(AssetsFileInstance file, AssetFileInfo info)
    {
        return new UnityObject(file, info, _manager.GetBaseField(file, info));
    }

    private AssetsFileInstance? ResolveFile(AssetsFileInstance file, int fileId)
    {
        if (fileId == 0)
        {
            return file;
        }

        var externals = file.file.Metadata.Externals;
        if (fileId < 1 || fileId > externals.Count)
        {
            return null;
        }

        var name = Path.GetFileName(externals[fileId - 1].PathName.Replace('\\', '/'));
        return File.Exists(Path.Join(DataDirectory, name)) ? Open(name) : null;
    }

    private void EnsureClassDatabase(AssetsFileInstance file)
    {
        if (_classDatabaseLoaded)
        {
            return;
        }

        var version = file.file.Metadata.UnityVersion;
        if (_manager.LoadClassDatabaseFromPackage(version) is null)
        {
            throw new GMConverterException($"Unity {version} is not in the class database.");
        }

        _classDatabaseLoaded = true;
    }
}
