using System.IO.Compression;
using GMConverter.Common;

namespace GMConverter.GameExplorer;

internal sealed class GameExplorerService
{
    public const string AutoProfileId = "auto";

    private static readonly GameProfile GenericProfile = new(
        "generic",
        "Generic supported files",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".opt"] = "opt",
            [".mdl"] = "mdl",
            [".psk"] = "psk",
            [".pskx"] = "psk",
            [".def"] = "mow"
        });

    private static readonly GameProfile MOWProfile = new(
        "mow",
        "Men of War",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".def"] = "mow",
            [".mdl"] = "mow"
        });

    public IReadOnlyList<GameProfile> Profiles { get; } = [MOWProfile, GenericProfile];

    public GameExplorerEntryResolver ResolveEntry(GameExplorerEntry entry)
    {
        if (entry.ArchivePath is null || entry.ArchiveEntryPath is null)
        {
            return new GameExplorerEntryResolver(entry.FilePath, entry.MaterialDirectory);
        }

        return ExtractArchiveEntry(entry);
    }

    public GameExplorerScanResult Scan(string gameDirectory, string profileId)
    {
        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(gameDirectory));
        if (!Directory.Exists(root))
        {
            throw new GMConverterException($"Game directory not found: {root}");
        }

        var profile = ResolveProfile(root, profileId);
        var looseEntries = EnumerateFiles(root)
            .Where(file => profile.SupportsExtension(Path.GetExtension(file)))
            .Select(file => CreateLooseEntry(root, profile, file));
        var archiveEntries = EnumerateArchiveEntries(root, profile);
        var entries = looseEntries
            .Concat(archiveEntries)
            .OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new GameExplorerScanResult(profile, entries);
    }

    private GameProfile ResolveProfile(string root, string profileId)
    {
        if (!string.Equals(profileId, AutoProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return Profiles.FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase))
                ?? throw new GMConverterException($"Unsupported game profile: {profileId}");
        }

        return LooksLikeMOW(root) ? MOWProfile : GenericProfile;
    }

    private static bool LooksLikeMOW(string root)
    {
        var hasDefinition = false;
        var hasPly = false;

        foreach (var file in EnumerateFiles(root))
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

    private static GameExplorerEntry CreateLooseEntry(string root, GameProfile profile, string file)
    {
        var relativePath = Path.GetRelativePath(root, file);
        return new GameExplorerEntry(
            relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            Path.GetFullPath(file),
            profile.GetInputFormat(Path.GetExtension(file)),
            root);
    }

    private static IEnumerable<GameExplorerEntry> EnumerateArchiveEntries(string root, GameProfile profile)
    {
        foreach (var archivePath in EnumerateFiles(root).Where(file => IsZipBackedPak(file)))
        {
            ZipArchive archive;
            try
            {
                archive = ZipFile.OpenRead(archivePath);
            }
            catch (InvalidDataException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            using (archive)
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Name) ||
                        !profile.SupportsExtension(Path.GetExtension(entry.Name)))
                    {
                        continue;
                    }

                    var archiveRelativePath = Path.GetRelativePath(root, archivePath).Replace(Path.DirectorySeparatorChar, '/');
                    var entryPath = NormalizeArchivePath(entry.FullName);
                    var displayPath = $"{archiveRelativePath}/{entryPath}";
                    var extractionRoot = GetExtractionRoot(archivePath);

                    yield return new GameExplorerEntry(
                        displayPath,
                        Path.Combine(extractionRoot, entryPath.Replace('/', Path.DirectorySeparatorChar)),
                        profile.GetInputFormat(Path.GetExtension(entry.Name)),
                        extractionRoot,
                        archivePath,
                        entryPath);
                }
            }
        }
    }

    private static GameExplorerEntryResolver ExtractArchiveEntry(GameExplorerEntry explorerEntry)
    {
        var archivePath = explorerEntry.ArchivePath
            ?? throw new InvalidOperationException("Archive path is required.");
        var archiveEntryPath = explorerEntry.ArchiveEntryPath
            ?? throw new InvalidOperationException("Archive entry path is required.");
        var extractionRoot = explorerEntry.MaterialDirectory;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            var normalizedEntryPath = NormalizeArchivePath(entry.FullName);
            if (!ShouldExtractEntry(normalizedEntryPath, archiveEntryPath))
            {
                continue;
            }

            var outputPath = Path.GetFullPath(Path.Combine(extractionRoot, normalizedEntryPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!outputPath.StartsWith(Path.GetFullPath(extractionRoot), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? extractionRoot);
            entry.ExtractToFile(outputPath, overwrite: true);
        }

        return new GameExplorerEntryResolver(explorerEntry.FilePath, extractionRoot);
    }

    private static bool ShouldExtractEntry(string entryPath, string selectedEntryPath)
    {
        var selectedDirectory = ArchiveDirectoryName(selectedEntryPath);
        var entryDirectory = ArchiveDirectoryName(entryPath);
        var extension = Path.GetExtension(entryPath);

        return string.Equals(entryPath, selectedEntryPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entryDirectory, selectedDirectory, StringComparison.OrdinalIgnoreCase) ||
            IsMOWSidecarExtension(extension);
    }

    private static bool IsMOWSidecarExtension(string extension)
    {
        return extension.Equals(".def", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mdl", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ply", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mtl", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tga", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".anm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsZipBackedPak(string file)
    {
        return string.Equals(Path.GetExtension(file), ".pak", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetExtractionRoot(string archivePath)
    {
        var safeArchiveName = string.Concat(
            Path.GetFileNameWithoutExtension(archivePath)
                .Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
        var archiveHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(archivePath))))[..12];
        return Path.Combine(Path.GetTempPath(), "GMConverter", "GameExplorer", $"{safeArchiveName}_{archiveHash}");
    }

    private static string NormalizeArchivePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static string ArchiveDirectoryName(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? string.Empty : path[..index];
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        Stack<string> pending = new([root]);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(directory);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var childDirectory in directories)
            {
                pending.Push(childDirectory);
            }
        }
    }
}
