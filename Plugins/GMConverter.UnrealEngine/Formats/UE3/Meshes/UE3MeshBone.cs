using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.UE3.Meshes;

/// <summary>A reference-skeleton bone, posed relative to its parent in Unreal's coordinates.</summary>
internal sealed record UE3MeshBone(string Name, int ParentIndex, Quaternion Rotation, Vector3 Position);
