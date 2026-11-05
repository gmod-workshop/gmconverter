using System.Numerics;
using System.Text.Json;

namespace GMConverter.UnrealEngine.Formats.PSK;

/// <summary>
/// Facts about a whole exported model that a PSK can't carry, which the explorer writes next to it
/// and the PSK importer reads back; it never leaves this plugin. <c>Forward</c> is the way the model
/// faces, in the PSK file's mirrored space. <c>FirstPerson</c> marks a view model, whose camera sits
/// at the origin.
/// </summary>
internal sealed record PSKModelSidecar(Vector3? Forward, bool FirstPerson = false)
{
    private const string _extension = ".model.json";
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, IncludeFields = true };

    public static PSKModelSidecar? TryRead(string meshPath)
    {
        var path = GetPath(meshPath);
        return File.Exists(path) ? JsonSerializer.Deserialize<PSKModelSidecar>(File.ReadAllText(path), _jsonOptions) : null;
    }

    public void Write(string meshPath)
    {
        File.WriteAllText(GetPath(meshPath), JsonSerializer.Serialize(this, _jsonOptions));
    }

    private static string GetPath(string meshPath) => Path.ChangeExtension(meshPath, _extension);
}
