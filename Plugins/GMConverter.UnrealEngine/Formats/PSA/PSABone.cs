using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.PSA;

internal readonly record struct PSABone(
    string Name,
    int Flags,
    int ChildrenCount,
    int ParentIndex,
    Quaternion Rotation,
    Vector3 Location,
    float Length,
    Vector3 Size);
