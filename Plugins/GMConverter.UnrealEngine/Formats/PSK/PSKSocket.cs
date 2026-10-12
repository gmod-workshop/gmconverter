using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.PSK;

/// <param name="Name">The socket name.</param>
/// <param name="Bone">The bone it is attached to, or null for the mesh origin (static meshes).</param>
/// <param name="Location">Its offset from the bone or origin.</param>
/// <param name="Rotation">Its rotation relative to the bone or origin.</param>
internal sealed record PSKSocket(string Name, string? Bone, Vector3 Location, Quaternion Rotation);
