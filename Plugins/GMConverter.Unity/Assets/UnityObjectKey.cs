using System.Globalization;

namespace GMConverter.Unity.Assets;

/// <summary>Identifies an object across the serialized files of one build: file name plus path id.</summary>
internal readonly record struct UnityObjectKey(string FileName, long PathId)
{
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{FileName}:{PathId}");
    }

    /// <summary>Reads a key written by <see cref="ToString"/>.</summary>
    public static UnityObjectKey Parse(string text)
    {
        var separator = text.LastIndexOf(':');
        if (separator <= 0 || !long.TryParse(text.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pathId))
        {
            throw new FormatException($"Not a Unity object key: {text}");
        }

        return new UnityObjectKey(text[..separator], pathId);
    }
}
