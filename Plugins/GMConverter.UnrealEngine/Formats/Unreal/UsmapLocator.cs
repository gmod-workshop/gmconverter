namespace GMConverter.UnrealEngine.Formats.Unreal;

// Finds a user-supplied .usmap for games without an online mappings source. Cooked UE5 games
// serialize "unversioned" properties that CUE4Parse cannot read without the game's type mappings,
// and those can only be produced by dumping the running game. The user drops the dumped file into
// the Paks directory or any parent up to the game's install root, so the search walks upward a few
// levels (Paks -> Content -> <Project> -> install root).
internal static class UsmapLocator
{
    private const int _maxParentLevels = 3;

    public static string? Find(string archiveDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(archiveDirectory));
        for (var level = 0; directory is not null && level <= _maxParentLevels; level++, directory = directory.Parent)
        {
            if (!directory.Exists)
            {
                continue;
            }

            // Several dumps may sit side by side after game updates; the newest one matches the
            // current build.
            var newest = directory
                .EnumerateFiles("*.usmap", SearchOption.TopDirectoryOnly)
                .MaxBy(file => file.LastWriteTimeUtc);
            if (newest is not null)
            {
                return newest.FullName;
            }
        }

        return null;
    }
}
