namespace GMConverter.GameExplorer;

internal sealed record GameProfile(
    string Id,
    string DisplayName,
    IReadOnlyDictionary<string, string> ModelExtensions)
{
    public bool SupportsExtension(string extension)
    {
        return ModelExtensions.ContainsKey(extension);
    }

    public string GetInputFormat(string extension)
    {
        return ModelExtensions[extension];
    }
}
