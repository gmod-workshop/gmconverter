using System.IO.Compression;
using GMConverter.Common;
using GMConverter.Formats.MOW;

namespace GMConverter.Explorer;

internal sealed class MOWExplorer : IExplorer
{
    private static readonly IReadOnlyDictionary<string, string> ModelExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".def"] = "mow",
            [".mdl"] = "mow"
        };

    private readonly Dictionary<string, IReadOnlyList<string>> archivePathCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<ArchiveEntryInfo>> archiveEntryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<TextureArchiveEntry>> textureEntryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object cacheLock = new();

    public string Id => "mow";

    public string DisplayName => "Men of War";

    public bool Supports(ExplorerTarget target)
    {
        return target.IsDirectory && LooksLikeMOW(target.FullPath) ||
            target.IsFile && IsPakArchive(target.FullPath);
    }

    public IReadOnlyList<ExplorerFileEntry> Scan(ExplorerTarget target)
    {
        if (target.IsFile)
        {
            var fileRoot = Path.GetDirectoryName(target.FullPath) ?? Environment.CurrentDirectory;
            return EnumerateArchiveEntries(fileRoot, target.FullPath)
                .OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var root = target.FullPath;
        var looseEntries = ExplorerFileSystem.EnumerateFiles(root)
            .Where(file => SupportsExtension(Path.GetExtension(file)))
            .Select(file => CreateLooseEntry(root, file));
        var archiveEntries = GetPakArchives(root)
            .SelectMany(archivePath => EnumerateArchiveEntries(root, archivePath));

        return looseEntries
            .Concat(archiveEntries)
            .OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ExplorerResolvedEntry ResolveEntry(ExplorerFileEntry fileEntry)
    {
        if (fileEntry.ArchivePath is null || fileEntry.ArchiveEntryPath is null)
        {
            return new ExplorerResolvedEntry(fileEntry.FilePath, fileEntry.MaterialDirectory);
        }

        return ExtractArchiveEntry(fileEntry);
    }

    public void ClearCaches()
    {
        lock (cacheLock)
        {
            archivePathCache.Clear();
            archiveEntryCache.Clear();
            textureEntryCache.Clear();
        }
    }

    private static bool LooksLikeMOW(string root)
    {
        var hasDefinition = false;
        var hasPly = false;

        foreach (var file in ExplorerFileSystem.EnumerateFiles(root))
        {
            var extension = Path.GetExtension(file);
            hasDefinition |= string.Equals(extension, ".def", StringComparison.OrdinalIgnoreCase);
            hasPly |= string.Equals(extension, ".ply", StringComparison.OrdinalIgnoreCase);

            if (hasDefinition && hasPly)
            {
                return true;
            }
        }

        return false;
    }

    private ExplorerFileEntry CreateLooseEntry(string root, string file)
    {
        var relativePath = Path.GetRelativePath(root, file);
        return new ExplorerFileEntry(
            relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            Path.GetFullPath(file),
            GetInputFormat(Path.GetExtension(file)),
            root,
            root,
            ExplorerId: Id);
    }

    private IEnumerable<ExplorerFileEntry> EnumerateArchiveEntries(string root, string archivePath)
    {
        foreach (var entry in GetArchiveEntries(archivePath))
        {
            if (string.IsNullOrWhiteSpace(entry.Name) ||
                !SupportsExtension(Path.GetExtension(entry.Name)))
            {
                continue;
            }

            var archiveRelativePath = Path.GetRelativePath(root, archivePath).Replace(Path.DirectorySeparatorChar, '/');
            var entryPath = entry.NormalizedPath;
            var displayPath = $"{archiveRelativePath}/{entryPath}";
            var extractionRoot = ExplorerFileSystem.GetExtractionRoot(archivePath);

            yield return new ExplorerFileEntry(
                displayPath,
                Path.Combine(extractionRoot, entryPath.Replace('/', Path.DirectorySeparatorChar)),
                GetInputFormat(Path.GetExtension(entry.Name)),
                extractionRoot,
                root,
                archivePath,
                entryPath,
                Id);
        }
    }

    private ExplorerResolvedEntry ExtractArchiveEntry(ExplorerFileEntry fileEntry)
    {
        var archivePath = fileEntry.ArchivePath
            ?? throw new InvalidOperationException("Archive path is required.");
        var archiveEntryPath = fileEntry.ArchiveEntryPath
            ?? throw new InvalidOperationException("Archive entry path is required.");
        var extractionRoot = fileEntry.MaterialDirectory;

        ExtractArchiveEntries(
            archivePath,
            extractionRoot,
            entryPath => ShouldExtractEntry(entryPath, archiveEntryPath));

        ExtractSearchArchiveTextures(
            fileEntry.SearchRoot,
            archivePath,
            extractionRoot,
            FindMOWTextureReferences(extractionRoot));

        return new ExplorerResolvedEntry(fileEntry.FilePath, extractionRoot);
    }

    private static IReadOnlySet<string> FindMOWTextureReferences(string extractionRoot)
    {
        HashSet<string> textureReferences = new(StringComparer.OrdinalIgnoreCase);

        foreach (var materialPath in ExplorerFileSystem.EnumerateFiles(extractionRoot)
                     .Where(file => string.Equals(Path.GetExtension(file), ".mtl", StringComparison.OrdinalIgnoreCase)))
        {
            MOWMaterialFile materialFile;
            try
            {
                materialFile = MOWMaterialFile.Read(materialPath);
            }
            catch (GMConverterException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            AddTextureReference(textureReferences, materialFile.DiffuseTexture);
            AddTextureReference(textureReferences, materialFile.SpecularTexture);
            AddTextureReference(textureReferences, materialFile.NormalTexture);
        }

        return textureReferences;
    }

    private static void AddTextureReference(HashSet<string> textureReferences, string? textureReference)
    {
        if (!string.IsNullOrWhiteSpace(textureReference))
        {
            textureReferences.Add(NameHelpers.SanitizeMaterialName(Path.GetFileNameWithoutExtension(textureReference)));
        }
    }

    private void ExtractSearchArchiveTextures(
        string searchRoot,
        string selectedArchivePath,
        string extractionRoot,
        IReadOnlySet<string> textureReferences)
    {
        if (textureReferences.Count == 0)
        {
            return;
        }

        var textureEntries = GetTextureEntries(searchRoot)
            .Where(entry => !string.Equals(Path.GetFullPath(entry.ArchivePath), Path.GetFullPath(selectedArchivePath), StringComparison.OrdinalIgnoreCase))
            .Where(entry => MatchesTextureReference(textureReferences, entry.TextureName))
            .GroupBy(entry => entry.ArchivePath, StringComparer.OrdinalIgnoreCase);

        foreach (var archiveGroup in textureEntries)
        {
            ExtractArchiveEntries(
                archiveGroup.Key,
                extractionRoot,
                archiveGroup.ToDictionary(entry => entry.FullName, entry => TextureOutputPath(extractionRoot, entry.NormalizedPath), StringComparer.OrdinalIgnoreCase));
        }
    }

    private static bool ShouldExtractEntry(string entryPath, string selectedEntryPath)
    {
        var selectedDirectory = ExplorerFileSystem.ArchiveDirectoryName(selectedEntryPath);
        var entryDirectory = ExplorerFileSystem.ArchiveDirectoryName(entryPath);

        return string.Equals(entryPath, selectedEntryPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entryDirectory, selectedDirectory, StringComparison.OrdinalIgnoreCase) ||
            entryDirectory.StartsWith($"{selectedDirectory}/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesTextureReference(IReadOnlySet<string> textureReferences, string textureName)
    {
        return textureReferences.Contains(textureName) ||
            textureReferences.Any(reference => reference.EndsWith(textureName, StringComparison.OrdinalIgnoreCase));
    }

    private static string TextureOutputPath(string extractionRoot, string entryPath)
    {
        var fileName = Path.GetFileName(entryPath);
        var directoryHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(ExplorerFileSystem.ArchiveDirectoryName(entryPath))))[..12];

        return Path.Combine(extractionRoot, "__archive_textures", directoryHash, fileName);
    }

    private IReadOnlyList<string> GetPakArchives(string searchRoot)
    {
        var cacheKey = Path.GetFullPath(searchRoot);
        lock (cacheLock)
        {
            if (archivePathCache.TryGetValue(cacheKey, out var archivePaths))
            {
                return archivePaths;
            }
        }

        var discoveredArchivePaths = ExplorerFileSystem.EnumerateFiles(searchRoot)
            .Where(IsPakArchive)
            .Select(Path.GetFullPath)
            .ToArray();

        lock (cacheLock)
        {
            archivePathCache[cacheKey] = discoveredArchivePaths;
        }

        return discoveredArchivePaths;
    }

    private IReadOnlyList<ArchiveEntryInfo> GetArchiveEntries(string archivePath)
    {
        var cacheKey = ArchiveCacheKey(archivePath);
        lock (cacheLock)
        {
            if (archiveEntryCache.TryGetValue(cacheKey, out var entries))
            {
                return entries;
            }
        }

        var discoveredEntries = ExplorerFileSystem.ReadArchiveEntries(archivePath)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Select(entry => new ArchiveEntryInfo(
                entry.FullName,
                entry.Name,
                ExplorerFileSystem.NormalizeArchivePath(entry.FullName)))
            .ToArray();

        lock (cacheLock)
        {
            archiveEntryCache[cacheKey] = discoveredEntries;
        }

        return discoveredEntries;
    }

    private IReadOnlyList<TextureArchiveEntry> GetTextureEntries(string searchRoot)
    {
        var cacheKey = Path.GetFullPath(searchRoot);
        lock (cacheLock)
        {
            if (textureEntryCache.TryGetValue(cacheKey, out var textureEntries))
            {
                return textureEntries;
            }
        }

        var discoveredTextureEntries = GetPakArchives(searchRoot)
            .SelectMany(archivePath => GetArchiveEntries(archivePath)
                .Where(entry => IsImageExtension(Path.GetExtension(entry.NormalizedPath)))
                .Select(entry => new TextureArchiveEntry(
                    archivePath,
                    entry.FullName,
                    entry.NormalizedPath,
                    NameHelpers.SanitizeMaterialName(Path.GetFileNameWithoutExtension(entry.NormalizedPath)))))
            .ToArray();

        lock (cacheLock)
        {
            textureEntryCache[cacheKey] = discoveredTextureEntries;
        }

        return discoveredTextureEntries;
    }

    private void ExtractArchiveEntries(
        string archivePath,
        string extractionRoot,
        Func<string, bool> shouldExtract)
    {
        var outputPathsByEntryName = GetArchiveEntries(archivePath)
            .Where(entry => shouldExtract(entry.NormalizedPath))
            .ToDictionary(
                entry => entry.FullName,
                entry => Path.GetFullPath(Path.Combine(
                    extractionRoot,
                    entry.NormalizedPath.Replace('/', Path.DirectorySeparatorChar))),
                StringComparer.OrdinalIgnoreCase);

        ExtractArchiveEntries(archivePath, extractionRoot, outputPathsByEntryName);
    }

    private static void ExtractArchiveEntries(
        string archivePath,
        string extractionRoot,
        IReadOnlyDictionary<string, string> outputPathsByEntryName)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(archivePath);
        }
        catch (InvalidDataException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }

        using (archive)
        {
            foreach (var (entryName, outputPath) in outputPathsByEntryName)
            {
                var entry = archive.GetEntry(entryName);
                if (entry is null)
                {
                    continue;
                }

                var fullOutputPath = Path.GetFullPath(outputPath);
                if (!fullOutputPath.StartsWith(Path.GetFullPath(extractionRoot), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath) ?? extractionRoot);
                if (File.Exists(fullOutputPath) &&
                    new FileInfo(fullOutputPath).Length == entry.Length)
                {
                    continue;
                }

                entry.ExtractToFile(fullOutputPath, overwrite: true);
            }
        }
    }

    private static bool IsImageExtension(string extension)
    {
        return extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tga", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static string ArchiveCacheKey(string archivePath)
    {
        var fullPath = Path.GetFullPath(archivePath);
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

    private static bool SupportsExtension(string extension)
    {
        return ModelExtensions.ContainsKey(extension);
    }

    private static bool IsPakArchive(string file)
    {
        return string.Equals(Path.GetExtension(file), ".pak", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetInputFormat(string extension)
    {
        return ModelExtensions.TryGetValue(extension, out var inputFormat)
            ? inputFormat
            : throw new GMConverterException($"Unsupported model extension: {extension}");
    }

    private sealed record ArchiveEntryInfo(
        string FullName,
        string Name,
        string NormalizedPath);

    private sealed record TextureArchiveEntry(
        string ArchivePath,
        string FullName,
        string NormalizedPath,
        string TextureName);
}
