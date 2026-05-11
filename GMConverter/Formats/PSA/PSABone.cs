using System.Numerics;

namespace GMConverter.Formats.PSA;

internal readonly record struct PSABone(
    string Name,
    int Flags,
    int ChildrenCount,
    int ParentIndex,
    Quaternion Rotation,
    Vector3 Location,
    float Length,
    Vector3 Size);
