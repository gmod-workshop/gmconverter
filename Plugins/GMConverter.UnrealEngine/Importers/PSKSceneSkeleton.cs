using System.Numerics;
using GMConverter.SDK.Geometry;

namespace GMConverter.UnrealEngine.Importers;

// Combines the skeletons of a scene's skinned parts into one. Modular UE characters split body,
// head and gear into separate skeletal meshes that share a skeleton asset, so bones are matched
// by name: the first part to introduce a bone defines its bind pose, and every part's vertex
// weights are re-pointed at the merged bone indices. Boneless parts (static .pskx meshes) pass
// through unchanged and end up bound to the root by the exporters. So do static meshes whose
// only "bones" are sockets (e.g. engine FX attach points): with no weights they are not a rig.
internal static class PSKSceneSkeleton
{
    public static (Skeleton? Skeleton, IReadOnlyList<Model> Parts) Merge(IReadOnlyList<Model> parts)
    {
        List<Bone> bones = [];
        Dictionary<string, int> indexByName = new(StringComparer.OrdinalIgnoreCase);
        var merged = new Model[parts.Count];

        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            if (part.Skeleton is not { Bones.Count: > 0 } skeleton || !IsSkinned(part))
            {
                // Without a rig the part's bones are dropped, so its bone attachments move to
                // the model origin at their bind-pose placement.
                merged[partIndex] = part.Skeleton is { } unusedSkeleton && part.Attachments is { Count: > 0 } attachments
                    ? part with { Attachments = [.. attachments.Select(attachment => ToModelSpace(attachment, unusedSkeleton))] }
                    : part;
                continue;
            }

            var boneMap = new int[skeleton.Bones.Count];
            var isIdentity = true;
            foreach (var bone in skeleton.Bones)
            {
                var name = bone.Name.TrimEnd();
                if (!indexByName.TryGetValue(name, out var mergedIndex))
                {
                    mergedIndex = bones.Count;
                    var parentIndex = bone.ParentIndex >= 0 && bone.ParentIndex < bone.Index
                        ? boneMap[bone.ParentIndex]
                        : -1;
                    bones.Add(bone with { Index = mergedIndex, ParentIndex = parentIndex });
                    indexByName.Add(name, mergedIndex);
                }

                boneMap[bone.Index] = mergedIndex;
                isIdentity &= mergedIndex == bone.Index;
            }

            merged[partIndex] = isIdentity
                ? part
                : part with
                {
                    Meshes = [.. part.Meshes.Select(mesh => RemapWeights(mesh, boneMap))],
                    Attachments = part.Attachments?
                        .Select(attachment => attachment.BoneIndex is { } boneIndex ? attachment with { BoneIndex = boneMap[boneIndex] } : attachment)
                        .ToArray(),
                };
        }

        return (bones.Count == 0 ? null : new Skeleton(bones), merged);
    }

    private static bool IsSkinned(Model part)
    {
        return part.Meshes.Any(mesh => mesh.Vertices.Any(vertex => vertex.BoneWeights is { Count: > 0 }));
    }

    private static Attachment ToModelSpace(Attachment attachment, Skeleton skeleton)
    {
        if (attachment.BoneIndex is not { } boneIndex)
        {
            return attachment;
        }

        var world = ToMatrix(attachment.LocalTransform);
        for (var bone = skeleton.Bones[boneIndex]; ; bone = skeleton.Bones[bone.ParentIndex])
        {
            world *= ToMatrix(bone.LocalBindPose);
            if (bone.ParentIndex < 0)
            {
                break;
            }
        }

        Matrix4x4.Decompose(world, out _, out var rotation, out var translation);
        return attachment with { BoneIndex = null, LocalTransform = new Transform(translation, rotation, Vector3.One) };
    }

    private static Matrix4x4 ToMatrix(Transform transform)
    {
        return Matrix4x4.CreateScale(transform.Scale) *
            Matrix4x4.CreateFromQuaternion(transform.Rotation) *
            Matrix4x4.CreateTranslation(transform.Translation);
    }

    private static Mesh RemapWeights(Mesh mesh, int[] boneMap)
    {
        var vertices = mesh.Vertices
            .Select(vertex => vertex.BoneWeights is null
                ? vertex
                : vertex with
                {
                    BoneWeights = [.. vertex.BoneWeights.Select(weight => weight with { BoneIndex = boneMap[weight.BoneIndex] })]
                })
            .ToArray();
        return mesh with { Vertices = vertices };
    }
}
