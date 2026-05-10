namespace GMConverter.Geometry;

internal sealed record Bone(
    int Index,
    string Name,
    int ParentIndex,
    Transform LocalBindPose);