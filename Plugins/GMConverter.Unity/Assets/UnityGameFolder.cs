namespace GMConverter.Unity.Assets;

/// <summary>
/// Finds a Unity player build's data folder (<c>&lt;Game&gt;_Data</c>, the folder holding
/// <c>globalgamemanagers</c>) from a path the user picked: the install folder, the data folder
/// itself, or a file inside either.
/// </summary>
internal static class UnityGameFolder
{
    public const string GameManagersFileName = "globalgamemanagers";

    /// <summary>The data folder for <paramref name="path"/>, or null when it is not part of a Unity build.</summary>
    public static string? FindDataDirectory(string path)
    {
        var directory = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        if (directory is null || !Directory.Exists(directory))
        {
            return null;
        }

        if (IsDataDirectory(directory))
        {
            return Path.GetFullPath(directory);
        }

        try
        {
            return Directory.EnumerateDirectories(directory, "*_Data").FirstOrDefault(IsDataDirectory) is { } dataDirectory
                ? Path.GetFullPath(dataDirectory)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool IsDataDirectory(string directory)
    {
        return File.Exists(Path.Join(directory, GameManagersFileName));
    }

    /// <summary>
    /// The serialized files of a player build that can hold models and sounds: the scenes
    /// (<c>level0</c>, <c>level1</c>, ...), the shared assets of each scene and the Resources
    /// folder, in build order.
    /// </summary>
    public static IReadOnlyList<string> GetAssetFileNames(string dataDirectory)
    {
        return Directory.EnumerateFiles(dataDirectory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(IsAssetFileName)
            .OrderBy(FileOrder)
            .ThenBy(name => name.Length)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>The scene index of a <c>levelN</c> file, or null for any other file.</summary>
    public static int? GetSceneIndex(string fileName)
    {
        return fileName.StartsWith("level", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(fileName.AsSpan(5), out var index)
            ? index
            : null;
    }

    private static bool IsAssetFileName(string name)
    {
        return GetSceneIndex(name) is not null ||
            name.Equals("resources.assets", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("sharedassets", StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith(".assets", StringComparison.OrdinalIgnoreCase);
    }

    private static int FileOrder(string name)
    {
        return GetSceneIndex(name) is not null ? 0 : name.StartsWith("sharedassets", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }
}
