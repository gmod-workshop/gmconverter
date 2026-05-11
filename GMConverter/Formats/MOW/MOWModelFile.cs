namespace GMConverter.Formats.MOW;

internal sealed record MOWModelFile(string Path, MOWNode Root)
{
    public static MOWModelFile Read(string path)
    {
        return new MOWModelFile(System.IO.Path.GetFullPath(path), MOWTextParser.ParseFile(path));
    }
}
