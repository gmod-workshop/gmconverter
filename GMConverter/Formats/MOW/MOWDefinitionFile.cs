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
        var modelPaths = ResolveModelPaths();
        if (modelPaths.Count == 0)
        {
            throw new GMConverterException($"Men of War DEF does not contain an Extension node: {Path}");
        }

        if (modelPaths.Count > 1)
        {
            throw new GMConverterException(
                $"Men of War DEF contains multiple Extension model references, which is not supported yet: " +
                string.Join(", ", modelPaths));
        }

        return modelPaths[0];
    }

    public IReadOnlyList<string> ResolveModelPaths()
    {
        return Root
            .Descendants("Extension")
            .Where(node => node.Values.Count > 0)
            .Select(node => node.Values[0])
            .Where(modelPath => !string.IsNullOrWhiteSpace(modelPath))
            .Select(modelPath => System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path) ?? ".", modelPath)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
