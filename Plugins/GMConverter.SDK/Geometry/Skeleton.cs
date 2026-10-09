namespace GMConverter.SDK.Geometry;

public sealed record Skeleton(IReadOnlyList<Bone> Bones)
{
    public Bone? Root => Bones.FirstOrDefault(bone => bone.ParentIndex < 0);
}
