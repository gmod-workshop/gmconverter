using System.Text.Json;
using System.Text.Json.Serialization;

namespace GMConverter.Unity.Assets;

/// <summary>
/// A <c>.unityasset</c> file: a pointer to an object inside a Unity build, written by the explorer
/// for the importer. <see cref="Object"/> is a root transform (a prefab or scene object) or a mesh;
/// <see cref="MaterialRenderer"/> optionally names a renderer whose materials a bare mesh uses.
/// </summary>
internal sealed record UnityAssetReference(
    [property: JsonPropertyName("dataDirectory")] string DataDirectory,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("materialRenderer")] string? MaterialRenderer = null)
{
    public const string FileExtension = ".unityasset";

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public UnityObjectKey ObjectKey => UnityObjectKey.Parse(Object);

    public UnityObjectKey? MaterialRendererKey => MaterialRenderer is null ? null : UnityObjectKey.Parse(MaterialRenderer);

    public static UnityAssetReference Read(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<UnityAssetReference>(stream, _jsonOptions)
            ?? throw new InvalidDataException($"Empty Unity asset reference: {path}");
    }

    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(this, _jsonOptions));
    }
}
