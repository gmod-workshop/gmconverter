namespace GMConverter.Common;

internal static class NameHelpers
{
    public static string? GetVersionedTextureName(IList<string> textures, int textureVersion)
    {
        if (textures.Count == 0)
        {
            return null;
        }

        var selected = textureVersion < 0 || textureVersion >= textures.Count ? textures.Count - 1 : textureVersion;
        return textures[selected];
    }

    public static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
        return string.IsNullOrWhiteSpace(sanitized) ? "model" : sanitized;
    }

    public static string SanitizeMaterialName(string value)
    {
        var sanitized = string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? char.ToLowerInvariant(c) : '_')).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "material" : sanitized;
    }
}
