using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.UE3.Meshes;

/// <summary>
/// A named attachment point on a bone. <c>Rotation</c> is Unreal's pitch, yaw and roll in 16-bit
/// angle units (65536 per turn).
/// </summary>
internal sealed record UE3MeshSocket(string Name, string BoneName, Vector3 Location, (int Pitch, int Yaw, int Roll) Rotation, Vector3 Scale);
