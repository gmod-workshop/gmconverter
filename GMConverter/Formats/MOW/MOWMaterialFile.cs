namespace GMConverter.Formats.MOW;

internal sealed record MOWMaterialFile(
    string Path,
    string Name,
    string? DiffuseTexture,
    string? SpecularTexture,
    string? NormalTexture,
    bool UsesAlpha)
{
    public static MOWMaterialFile Read(string path)
    {
        var root = MOWTextParser.ParseFile(path);
        var material = root.FirstChild("material") ?? (root.Children.Count > 0 ? root.Children[0] : null);
        var name = System.IO.Path.GetFileNameWithoutExtension(path);

        return new MOWMaterialFile(
            System.IO.Path.GetFullPath(path),
            name,
            TextureName(material?.FirstChild("diffuse")),
            TextureName(material?.FirstChild("specular")),
            TextureName(material?.FirstChild("bump")) ?? TextureName(material?.FirstChild("normal")),
            UsesAlphaBlend(material?.FirstChild("blend")));
    }

    private static string? TextureName(MOWNode? node)
    {
        if (node is null || node.Values.Count == 0)
        {
            return null;
        }

        var value = node.Values[0].Trim();
        if (value.Length == 0 || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (value.StartsWith('$'))
        {
            value = value[1..];
        }

        value = value.Replace('\\', '/');
        var slashIndex = value.LastIndexOf('/');
        return slashIndex >= 0 ? value[(slashIndex + 1)..] : value;
    }

    private static bool UsesAlphaBlend(MOWNode? node)
    {
        if (node is null || node.Values.Count == 0)
        {
            return false;
        }

        var value = node.Values[0].Trim();
        return value.Length > 0 && !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase);
    }
}
