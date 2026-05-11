using GMConverter.Common;

namespace GMConverter.Formats.MOW;

internal sealed record MOWDefinitionFile(string Path, MOWNode Root)
{
    public static MOWDefinitionFile Read(string path)
    {
        return new MOWDefinitionFile(System.IO.Path.GetFullPath(path), MOWTextParser.ParseFile(path));
    }

    public string ResolveModelPath()
    {
        var extension = Root
            .Descendants("Extension")
            .FirstOrDefault(node => node.Values.Count > 0);

        if (extension is null)
        {
            throw new GMConverterException($"Men of War DEF does not contain an Extension node: {Path}");
        }

        var modelPath = extension.Values[0];
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path) ?? ".", modelPath));
    }
}
