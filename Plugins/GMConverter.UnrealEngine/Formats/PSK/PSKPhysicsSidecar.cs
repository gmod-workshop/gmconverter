using System.Text.Json;
using System.Text.Json.Serialization;

namespace GMConverter.UnrealEngine.Formats.PSK;

/// <summary>
/// A skeletal mesh's physics asset (per-bone collision bodies and the constraints between them),
/// which the explorer writes next to the PSK because the PSK format has no place for it.
/// Positions and rotations are in the PSK file's mirrored space, like the mesh's own bones.
/// </summary>
internal sealed record PSKPhysicsSidecar(IReadOnlyList<PSKPhysicsBody> Bodies, IReadOnlyList<PSKPhysicsJoint> Joints)
{
    private const string _extension = ".physics.json";
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static PSKPhysicsSidecar? TryRead(string meshPath)
    {
        var path = GetPath(meshPath);
        return File.Exists(path) ? JsonSerializer.Deserialize<PSKPhysicsSidecar>(File.ReadAllText(path), _jsonOptions) : null;
    }

    public void Write(string meshPath)
    {
        File.WriteAllText(GetPath(meshPath), JsonSerializer.Serialize(this, _jsonOptions));
    }

    private static string GetPath(string meshPath) => Path.ChangeExtension(meshPath, _extension);
}
