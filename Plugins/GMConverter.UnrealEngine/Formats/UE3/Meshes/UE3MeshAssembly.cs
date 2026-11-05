using System.Numerics;
using GMConverter.SDK.Common;

namespace GMConverter.UnrealEngine.Formats.UE3.Meshes;

/// <summary>
/// Combines skeletal meshes the way a game attaches one to another's bone at run time, such as a
/// weapon held by first-person arms: the part's skeleton hangs under the bone, and its vertices
/// move into the body's space at the bind pose.
/// </summary>
internal static class UE3MeshAssembly
{
    /// <summary>
    /// The body with <paramref name="part"/> attached to <paramref name="boneName"/>. The part's bones
    /// get <paramref name="bonePrefix"/> so names shared with the body (both have a root) stay apart.
    /// </summary>
    public static UE3SkeletalMesh Attach(UE3SkeletalMesh body, UE3SkeletalMesh part, string boneName, string bonePrefix)
    {
        var parentIndex = IndexOf(body.Bones, boneName);
        if (parentIndex < 0)
        {
            throw new GMConverterException($"{body.Name} has no bone {boneName} to attach {part.Name} to.");
        }

        var parentWorld = GetBindWorld(body.Bones)[parentIndex];
        var boneOffset = body.Bones.Count;
        var bones = body.Bones.Concat(part.Bones.Select((bone, index) => bone with
        {
            Name = bonePrefix + bone.Name,
            ParentIndex = index == 0 || bone.ParentIndex == index ? parentIndex : bone.ParentIndex + boneOffset,
        })).ToList();

        var normalTransform = parentWorld with { M41 = 0, M42 = 0, M43 = 0 };
        var vertices = body.Vertices.Concat(part.Vertices.Select(vertex => vertex with
        {
            Position = Vector3.Transform(vertex.Position, parentWorld),
            Normal = Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, normalTransform)),
            Bones = [.. vertex.Bones.Select(bone => bone + boneOffset)],
        })).ToList();

        var vertexOffset = body.Vertices.Count;
        var indexOffset = body.Indices.Count;
        var materialOffset = body.Materials.Count;
        return new UE3SkeletalMesh(
            body.Name,
            bones,
            [.. body.Materials, .. part.Materials],
            [.. body.Sections, .. part.Sections.Select(section => section with
            {
                MaterialIndex = section.MaterialIndex + materialOffset,
                FirstIndex = section.FirstIndex + indexOffset,
            })],
            [.. body.Indices, .. part.Indices.Select(index => index + vertexOffset)],
            vertices,
            [.. body.Sockets, .. part.Sockets.Select(socket => socket with { BoneName = bonePrefix + socket.BoneName })]);
    }

    /// <summary>
    /// Each bone's bind pose in mesh space, composing local transforms down from the root. Unreal's
    /// quaternions turn the other way from <see cref="Matrix4x4.CreateFromQuaternion"/>'s, so they are conjugated.
    /// </summary>
    public static Matrix4x4[] GetBindWorld(IReadOnlyList<UE3MeshBone> bones)
    {
        var world = new Matrix4x4[bones.Count];
        for (var index = 0; index < bones.Count; index++)
        {
            var bone = bones[index];
            var local = Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(Quaternion.Normalize(bone.Rotation))) * Matrix4x4.CreateTranslation(bone.Position);
            world[index] = bone.ParentIndex >= 0 && bone.ParentIndex < index ? local * world[bone.ParentIndex] : local;
        }

        return world;
    }

    private static int IndexOf(IReadOnlyList<UE3MeshBone> bones, string name)
    {
        for (var index = 0; index < bones.Count; index++)
        {
            if (bones[index].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
