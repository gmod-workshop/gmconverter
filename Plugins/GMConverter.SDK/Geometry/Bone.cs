namespace GMConverter.SDK.Geometry;

public sealed record Bone(
    int Index,
    string Name,
    int ParentIndex,
    Transform LocalBindPose);
