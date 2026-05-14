namespace GMConverter.Formats.Unreal;

internal sealed class UnrealPackageResolver
{
    private static readonly HashSet<string> _packageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ukx",
        ".usx",
        ".utx",
        ".uax",
        ".u",
        ".umx",
        ".unr",
        ".ctm",
        ".upx"
    };

    private readonly Dictionary<string, UnrealPackageFile?> _packageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _searchRoot;
    private Dictionary<string, string>? _packagePaths;

    public UnrealPackageResolver(string searchRoot)
    {
        _searchRoot = searchRoot;
    }

    public UnrealResolvedObject Resolve(UnrealPackageFile sourcePackage, int packageIndex)
    {
        if (packageIndex > 0)
        {
            var export = sourcePackage.GetExport(packageIndex);
            return export is null
                ? UnrealResolvedObject.Missing(sourcePackage.GetObjectName(packageIndex), string.Empty)
                : new UnrealResolvedObject(
                    export.ObjectName,
                    sourcePackage.GetClassName(export),
                    sourcePackage,
                    export);
        }

        if (packageIndex < 0)
        {
            var import = sourcePackage.GetImport(packageIndex);
            if (import is null)
            {
                return UnrealResolvedObject.Missing(sourcePackage.GetObjectName(packageIndex), string.Empty);
            }

            var importPath = sourcePackage.GetFullImportName(import);
            var packageName = importPath
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            var importedPackage = string.IsNullOrWhiteSpace(packageName) ? null : GetPackage(packageName);
            var importedExport = importedPackage?.FindExport(import.ObjectName, import.ClassName);
            return new UnrealResolvedObject(
                import.ObjectName,
                import.ClassName,
                importedPackage,
                importedExport);
        }

        return UnrealResolvedObject.Missing("None", string.Empty);
    }

    private UnrealPackageFile? GetPackage(string packageName)
    {
        if (_packageCache.TryGetValue(packageName, out var package))
        {
            return package;
        }

        var packagePath = FindPackagePath(packageName);
        if (packagePath is null)
        {
            _packageCache[packageName] = null;
            return null;
        }

        try
        {
            package = UnrealPackageFile.Read(packagePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Common.GMConverterException)
        {
            package = null;
        }

        _packageCache[packageName] = package;
        return package;
    }

    private string? FindPackagePath(string packageName)
    {
        _packagePaths ??= BuildPackageIndex();
        return _packagePaths.TryGetValue(packageName, out var packagePath) ? packagePath : null;
    }

    private Dictionary<string, string> BuildPackageIndex()
    {
        Dictionary<string, string> packages = new(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_searchRoot))
        {
            return packages;
        }

        foreach (var filePath in Directory.EnumerateFiles(_searchRoot, "*.*", SearchOption.AllDirectories))
        {
            if (!_packageExtensions.Contains(Path.GetExtension(filePath)))
            {
                continue;
            }

            packages.TryAdd(Path.GetFileNameWithoutExtension(filePath), filePath);
        }

        return packages;
    }
}

internal sealed record UnrealResolvedObject(
    string ObjectName,
    string ClassName,
    UnrealPackageFile? Package,
    UnrealPackageExport? Export)
{
    public static UnrealResolvedObject Missing(string objectName, string className)
    {
        return new UnrealResolvedObject(objectName, className, null, null);
    }
}
