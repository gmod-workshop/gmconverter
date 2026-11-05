using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.UE3.Meshes;

/// <summary>
/// A skinned vertex. <c>Bones</c> are indexes into the mesh's skeleton (already mapped from the
/// render chunk's own bone list) paired with <c>Weights</c> out of 255.
/// </summary>
internal sealed record UE3MeshVertex(Vector3 Position, Vector3 Normal, Vector2 Uv, int[] Bones, byte[] Weights);
