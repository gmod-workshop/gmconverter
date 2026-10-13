using System.Numerics;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Geometry;

namespace GMConverter.SourceEngine.Exporters;

/// <summary>
/// Turns a model to face +X, the direction Source models face: NPCs move and turn along their +X,
/// so a character facing anywhere else would walk sideways. Everything in model space turns with it
/// (vertices, root bones and their animation, attachments without a bone); what is relative to a
/// bone follows that bone.
/// </summary>
internal static class SourceFacing
{
    private const float _alignedTolerance = 0.0001f;

    public static Model FaceForward(Model model)
    {
        if (model.Forward is not { } forward || new Vector2(forward.X, forward.Y).LengthSquared() < _alignedTolerance)
        {
            return model;
        }

        var angle = -MathF.Atan2(forward.Y, forward.X);
        if (MathF.Abs(angle) < _alignedTolerance)
        {
            return model;
        }

        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);
        Transform Turn(Transform transform) => transform with
        {
            Translation = Vector3.Transform(transform.Translation, rotation),
            Rotation = Quaternion.Normalize(rotation * transform.Rotation),
        };

        var rootBones = model.Skeleton?.Bones.Where(bone => bone.ParentIndex < 0).Select(bone => bone.Index).ToHashSet() ?? [];
        var poses = new Dictionary<AnimationPose, AnimationPose>(ReferenceEqualityComparer.Instance);
        AnimationPose TurnPose(AnimationPose pose)
        {
            if (!poses.TryGetValue(pose, out var turned))
            {
                turned = pose with
                {
                    BoneTransforms = pose.BoneTransforms.ToDictionary(
                        entry => entry.Key,
                        entry => rootBones.Contains(entry.Key) ? Turn(entry.Value) : entry.Value),
                };
                poses[pose] = turned;
            }

            return turned;
        }

        IAnimationTrack TurnTrack(IAnimationTrack track) => track switch
        {
            BoneTransformTrack bone when rootBones.Contains(bone.BoneIndex) =>
                bone with { Keyframes = [.. bone.Keyframes.Select(key => key with { Transform = Turn(key.Transform) })] },
            ObjectTransformTrack objectTrack =>
                objectTrack with { Keyframes = [.. objectTrack.Keyframes.Select(key => key with { Transform = Turn(key.Transform) })] },
            _ => track,
        };

        Mesh TurnMesh(Mesh mesh) => mesh with
        {
            Vertices = [.. mesh.Vertices.Select(vertex => vertex with
            {
                Position = Vector3.Transform(vertex.Position, rotation),
                Normal = Vector3.Transform(vertex.Normal, rotation),
            })],
        };

        return model with
        {
            Meshes = [.. model.Meshes.Select(TurnMesh)],
            Lods = model.Lods is { } lods ? [.. lods.Select(lod => lod with { Meshes = [.. lod.Meshes.Select(TurnMesh)] })] : null,
            Skeleton = model.Skeleton is { } skeleton
                ? skeleton with
                {
                    Bones = [.. skeleton.Bones.Select(bone => bone.ParentIndex < 0 ? bone with { LocalBindPose = Turn(bone.LocalBindPose) } : bone)],
                }
                : null,
            Animations = model.Animations is { } clips
                ? [.. clips.Select(clip => clip with
                {
                    Tracks = [.. clip.Tracks.Select(TurnTrack)],
                    Layering = clip.Layering is { Base: { } basePose } layering ? layering with { Base = TurnPose(basePose) } : clip.Layering,
                })]
                : null,
            Attachments = model.Attachments is { } attachments
                ? [.. attachments.Select(attachment => attachment.BoneIndex is null ? attachment with { LocalTransform = Turn(attachment.LocalTransform) } : attachment)]
                : null,
            Forward = Vector3.UnitX,
        };
    }
}
