namespace GMConverter.Godot.Packs;

/// <summary>
/// Finds a Godot game's resource pack from a path the user picked: the pack itself, the
/// executable it is appended to, or the folder holding either.
/// </summary>
internal static class GodotGameFolder
{
    /// <summary>The file holding the pack for <paramref name="path"/>, or null when it is not a Godot game.</summary>
    public static string? FindPack(string path)
    {
        if (File.Exists(path))
        {
            return IsCandidate(path) && GodotPack.Contains(path) ? Path.GetFullPath(path) : null;
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        try
        {
            // A pack beside the executable wins; otherwise the largest executable holding one.
            return Directory.EnumerateFiles(path, "*" + GodotPack.FileExtension).FirstOrDefault(GodotPack.Contains) is { } pack
                ? Path.GetFullPath(pack)
                : Directory.EnumerateFiles(path)
                    .Where(file => IsExecutable(file) && GodotPack.Contains(file))
                    .OrderByDescending(file => new FileInfo(file).Length)
                    .Select(Path.GetFullPath)
                    .FirstOrDefault();
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

    private static bool IsCandidate(string path)
    {
        return path.EndsWith(GodotPack.FileExtension, StringComparison.OrdinalIgnoreCase) || IsExecutable(path);
    }

    // Windows executables, and Linux ones, which have no extension (x86_64 builds end in ".x86_64").
    private static bool IsExecutable(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".x86_64", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".arm64", StringComparison.OrdinalIgnoreCase) ||
            extension.Length == 0;
    }
}
