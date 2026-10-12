using System.Text.Json;

namespace GMConverter.UnrealEngine.Formats.PSK;

/// <summary>
/// A mesh's sockets (named attach points), which the explorer writes next to the PSK because
/// CUE4Parse would otherwise fold them into the skeleton as extra bones. Positions and rotations
/// are already in the PSK file's mirrored space, so they convert like the mesh's own bones.
/// </summary>
internal sealed record PSKSocketSidecar(IReadOnlyList<PSKSocket> Sockets)
{
    private const string _extension = ".sockets.json";
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, IncludeFields = true };

    public static PSKSocketSidecar? TryRead(string meshPath)
    {
        var path = GetPath(meshPath);
        return File.Exists(path) ? JsonSerializer.Deserialize<PSKSocketSidecar>(File.ReadAllText(path), _jsonOptions) : null;
    }

    public void Write(string meshPath)
    {
        File.WriteAllText(GetPath(meshPath), JsonSerializer.Serialize(this, _jsonOptions));
    }

    private static string GetPath(string meshPath) => Path.ChangeExtension(meshPath, _extension);
}
