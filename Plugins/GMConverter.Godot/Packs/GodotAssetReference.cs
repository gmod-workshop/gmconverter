using System.Text.Json;
using System.Text.Json.Serialization;

namespace GMConverter.Godot.Packs;

/// <summary>
/// A <c>.godotscene</c> file: a pointer to a scene inside a Godot game, written by the explorer
/// for the importer. <see cref="Scene"/> is the scene's project path, such as
/// <c>res://models/shotgun.glb</c> or <c>res://instances/item_beer can.tscn</c>.
/// </summary>
internal sealed record GodotAssetReference(
    [property: JsonPropertyName("pack")] string Pack,
    [property: JsonPropertyName("scene")] string Scene)
{
    public const string FileExtension = ".godotscene";

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public static GodotAssetReference Read(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<GodotAssetReference>(stream, _jsonOptions)
            ?? throw new InvalidDataException($"Empty Godot scene reference: {path}");
    }

    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(this, _jsonOptions));
    }
}
