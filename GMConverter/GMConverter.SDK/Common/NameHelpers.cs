namespace GMConverter.SDK.Common;

public static class NameHelpers
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

    /// <summary>
    /// Splits an asset, bone or socket name into lowercase words at separators and lower-to-upper
    /// case changes, so names in different conventions can be matched alike:
    /// <c>"LeftUpLeg"</c> gives left, up, leg; <c>"upperarm_l"</c> gives upperarm, l;
    /// <c>"A_B1_2HRifle_Run_F"</c> gives a, b1, 2hrifle, run, f.
    /// </summary>
    public static List<string> SplitWords(string name)
    {
        List<string> words = [];
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            if (current.Length > 0)
            {
                words.Add(current.ToString().ToLowerInvariant());
                current.Clear();
            }
        }

        for (var i = 0; i < name.Length; i++)
        {
            var character = name[i];
            if (!char.IsLetterOrDigit(character))
            {
                Flush();
                continue;
            }

            if (char.IsUpper(character) && i > 0 && char.IsLower(name[i - 1]))
            {
                Flush();
            }

            current.Append(character);
        }

        Flush();
        return words;
    }
}
