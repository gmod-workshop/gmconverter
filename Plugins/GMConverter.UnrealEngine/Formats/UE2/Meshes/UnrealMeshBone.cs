using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.UE2.Meshes;

internal sealed record UnrealMeshBone(
    int Index,
    string Name,
    uint Flags,
    int ParentIndex,
    int NumChildren,
    Vector3 Position,
    Quaternion Rotation,
    float Length,
    Vector3 Size);
