using System.Numerics;

namespace GMConverter.Formats.PSK;

internal readonly record struct PSKBone(
    string Name,
    int Flags,
    int ChildrenCount,
    int ParentIndex,
    Quaternion Rotation,
    Vector3 Location,
    float Length,
    Vector3 Size);
