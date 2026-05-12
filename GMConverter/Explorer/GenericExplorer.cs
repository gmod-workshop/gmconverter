using GMConverter.Common;

namespace GMConverter.Explorer;

internal sealed class GenericExplorer : IExplorer
{
    private static readonly IReadOnlyDictionary<string, string> ModelExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".opt"] = "opt",
            [".mdl"] = "mdl",
            [".psk"] = "psk",
            [".pskx"] = "psk",
            [".def"] = "mow"
        };

    public string Id => "generic";

    public string DisplayName => "Generic supported files";

    public bool Supports(ExplorerTarget target)
    {
        return target.IsDirectory ||
            target.IsFile && SupportsExtension(Path.GetExtension(target.FullPath));
    }

    public IReadOnlyList<ExplorerFileEntry> Scan(ExplorerTarget target)
    {
        if (target.IsFile)
        {
            return ScanFile(target.FullPath).OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        var root = target.FullPath;
        return ExplorerFileSystem.EnumerateFiles(root)
            .Where(file => SupportsExtension(Path.GetExtension(file)))
            .Select(file => CreateLooseEntry(root, file))
            .OrderBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ExplorerResolvedEntry ResolveEntry(ExplorerFileEntry fileEntry)
    {
        return new ExplorerResolvedEntry(fileEntry.FilePath, fileEntry.MaterialDirectory);
    }

    public void ClearCaches()
    {
    }

    private IEnumerable<ExplorerFileEntry> ScanFile(string file)
    {
        var root = Path.GetDirectoryName(file) ?? Environment.CurrentDirectory;
        if (SupportsExtension(Path.GetExtension(file)))
        {
            yield return CreateLooseEntry(root, file);
        }

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

    private static bool SupportsExtension(string extension)
    {
        return ModelExtensions.ContainsKey(extension);
    }

    private static string GetInputFormat(string extension)
    {
        return ModelExtensions.TryGetValue(extension, out var inputFormat)
            ? inputFormat
            : throw new GMConverterException($"Unsupported model extension: {extension}");
    }
}
