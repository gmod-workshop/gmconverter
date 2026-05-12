namespace GMConverter.Explorer;

internal sealed record ExplorerTarget
{
    public ExplorerTarget(string path)
    {
        FullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    public string FullPath { get; }

    public bool IsDirectory => Directory.Exists(FullPath);

    public bool IsFile => File.Exists(FullPath);
}
