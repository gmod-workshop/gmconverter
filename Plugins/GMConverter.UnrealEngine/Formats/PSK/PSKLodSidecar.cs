using System.Text.Json;

namespace GMConverter.UnrealEngine.Formats.PSK;

/// <summary>
/// A mesh's lower levels of detail, which the explorer writes next to its PSK: CUE4Parse writes
/// each level as a PSK of its own (<c>Name_LOD1.psk</c>, …) with nothing tying them together or
/// saying when each one takes over.
/// </summary>
internal sealed record PSKLodSidecar(IReadOnlyList<PSKLodEntry> Lods)
{
    private const string _extension = ".lods.json";
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public static PSKLodSidecar? TryRead(string meshPath)
    {
        var path = GetPath(meshPath);
        return File.Exists(path) ? JsonSerializer.Deserialize<PSKLodSidecar>(File.ReadAllText(path), _jsonOptions) : null;
    }

    public void Write(string meshPath)
    {
        File.WriteAllText(GetPath(meshPath), JsonSerializer.Serialize(this, _jsonOptions));
    }

    private static string GetPath(string meshPath) => System.IO.Path.ChangeExtension(meshPath, _extension);
}
