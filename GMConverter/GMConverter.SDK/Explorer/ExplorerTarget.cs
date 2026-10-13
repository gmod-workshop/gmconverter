namespace GMConverter.SDK.Explorer;

/// <summary>A folder or archive file an explorer is asked to scan.</summary>
public sealed record ExplorerTarget
{
    public ExplorerTarget(string path)
    {
        FullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    public string FullPath { get; }

    public bool IsDirectory => Directory.Exists(FullPath);

    public bool IsFile => File.Exists(FullPath);

    /// <summary>
    /// The <see cref="Profiles.IGameProfile.Id"/> of the game the host detected or the user chose for
    /// this target, or null when no profile claimed it.
    /// </summary>
    public string? GameId { get; init; }
}
