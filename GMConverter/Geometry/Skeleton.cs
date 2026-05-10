namespace GMConverter.Geometry;

internal sealed record Skeleton(IReadOnlyList<Bone> Bones)
{
    public Bone? Root => Bones.FirstOrDefault(bone => bone.ParentIndex < 0);
}

internal readonly record struct VertexBoneWeight(int BoneIndex, float Weight);
