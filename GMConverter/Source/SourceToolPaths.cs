using GMConverter.Common;

namespace GMConverter.Source;

internal sealed record SourceToolPaths(
    string? StudioMdlPath,
    string? VtexPath,
    string GameDirectory,
    string? EngineDirectory,
    GameInfo GameInfo)
{
    public bool CanCompileMaterials => !string.IsNullOrWhiteSpace(VtexPath);

    public bool CanCompileModel => !string.IsNullOrWhiteSpace(StudioMdlPath);

    public static SourceToolPaths Resolve(string? engineDirectory, string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new GMConverterException("Game directory is required.");
        }

        GameInfo gameInfo = GameInfo.Load(gameDirectory);
        string? fullEngineDirectory = string.IsNullOrWhiteSpace(engineDirectory) ? null : Path.GetFullPath(engineDirectory);

        if (fullEngineDirectory is not null && !Directory.Exists(fullEngineDirectory))
        {
            throw new GMConverterException($"Engine directory not found: {fullEngineDirectory}");
        }

        fullEngineDirectory = fullEngineDirectory is null ? gameInfo.InferEngineDirectory() : NormalizeEngineDirectory(fullEngineDirectory);
        string? fullStudioMdlPath = fullEngineDirectory is null ? null : TryFindEngineTool(fullEngineDirectory, "studiomdl.exe");
        string? fullVtexPath = fullEngineDirectory is null ? null : TryFindEngineTool(fullEngineDirectory, "vtex.exe");

        return new SourceToolPaths(fullStudioMdlPath, fullVtexPath, gameInfo.GameDirectory, fullEngineDirectory, gameInfo);
    }

    private static string NormalizeEngineDirectory(string engineDirectory)
    {
        var directory = new DirectoryInfo(engineDirectory);

        if (string.Equals(directory.Name, "win64", StringComparison.OrdinalIgnoreCase) && directory.Parent is not null)
        {
            directory = directory.Parent;
        }

        if (string.Equals(directory.Name, "bin", StringComparison.OrdinalIgnoreCase) && directory.Parent is not null)
        {
            directory = directory.Parent;
        }

        return directory.FullName;
    }

    private static string? TryFindEngineTool(string engineDirectory, string toolName)
    {
        string binPath = Path.Combine(engineDirectory, "bin", toolName);

        if (File.Exists(binPath))
        {
            return binPath;
        }

        string win64Path = Path.Combine(engineDirectory, "bin", "win64", toolName);
        return File.Exists(win64Path) ? win64Path : null;
    }
}
