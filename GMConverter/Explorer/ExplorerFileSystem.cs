using System.IO.Compression;

namespace GMConverter.Explorer;

internal static class ExplorerFileSystem
{
    public static IEnumerable<string> EnumerateFiles(string root)
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

    public static string GetExtractionRoot(string archivePath)
    {
        var safeArchiveName = string.Concat(
            Path.GetFileNameWithoutExtension(archivePath)
                .Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
        var archiveHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(archivePath))))[..12];
        return Path.Combine(Path.GetTempPath(), "GMConverter", "Explorer", $"{safeArchiveName}_{archiveHash}");
    }

    public static string NormalizeArchivePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    public static string ArchiveDirectoryName(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? string.Empty : path[..index];
    }

    public static IEnumerable<ZipArchiveEntry> ReadArchiveEntries(string archivePath)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(archivePath);
        }
        catch (InvalidDataException)
        {
            yield break;
        }
        catch (IOException)
        {
            yield break;
        }

        using (archive)
        {
            foreach (var entry in archive.Entries)
            {
                yield return entry;
            }
        }
    }

    public static void ExtractEntryToRoot(ZipArchiveEntry entry, string extractionRoot, string normalizedEntryPath)
    {
        var outputPath = Path.GetFullPath(Path.Combine(
            extractionRoot,
            normalizedEntryPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!outputPath.StartsWith(Path.GetFullPath(extractionRoot), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? extractionRoot);
        if (File.Exists(outputPath) &&
            new FileInfo(outputPath).Length == entry.Length)
        {
            return;
        }

        entry.ExtractToFile(outputPath, overwrite: true);
    }
}
