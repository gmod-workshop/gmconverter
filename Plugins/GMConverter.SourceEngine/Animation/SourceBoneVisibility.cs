using System.Numerics;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Geometry;

namespace GMConverter.SourceEngine.Animation;

/// <summary>
/// Hides bones in sequences. Source has no way to stop drawing part of a model during a sequence,
/// so a hidden bone, with everything skinned to it and below it, drops far beneath the model, out
/// of sight under the floor, and comes back when shown.
/// </summary>
internal static class SourceBoneVisibility
{
    // 512 units in meters: well under any floor, without stretching the sequence bounds too far.
    private const float _hiddenDrop = 512f / 39.3700787f;

    /// <summary>Whether any bone starts hidden, so the model needs an idle sequence with them hidden.</summary>
    public static bool HasHiddenBones(Skeleton skeleton)
    {
        return skeleton.Bones.Any(bone => bone.HiddenAtRest);
    }

    /// <summary>The bones hidden at <paramref name="frameIndex"/>: per the clip's visibility tracks, or hidden at rest when it has none.</summary>
    public static HashSet<int> HiddenBones(Skeleton skeleton, AnimationClip clip, int frameIndex)
    {
        var tracks = clip.Tracks.OfType<BoneVisibilityTrack>().ToDictionary(track => track.BoneIndex);
        HashSet<int> hidden = [];
        foreach (var bone in skeleton.Bones)
        {
            var visible = !bone.HiddenAtRest;
            if (tracks.TryGetValue(bone.Index, out var track))
            {
                foreach (var keyframe in track.Keyframes.OrderBy(keyframe => keyframe.TimeSeconds))
                {
                    if ((int)MathF.Round(keyframe.TimeSeconds * clip.FrameRate) > frameIndex)
                    {
                        break;
                    }

                    visible = keyframe.Visible;
                }
            }

            if (!visible)
            {
                hidden.Add(bone.Index);
            }
        }

        return hidden;
    }

    /// <summary>Drops each hidden bone whose parent is shown, moving its local transform so it lands straight below.</summary>
    public static void Apply(Skeleton skeleton, Dictionary<int, Transform> locals, HashSet<int> hidden)
    {
        if (hidden.Count == 0)
        {
            return;
        }

        var bones = skeleton.Bones.ToDictionary(bone => bone.Index);
        Dictionary<int, (Quaternion Rotation, Vector3 Scale)> worlds = [];
        foreach (var index in hidden.Where(index => bones.ContainsKey(index)))
        {
            var bone = bones[index];
            if (bone.ParentIndex >= 0 && hidden.Contains(bone.ParentIndex))
            {
                continue;
            }

            var (parentRotation, parentScale) = bone.ParentIndex >= 0 ? World(bone.ParentIndex) : (Quaternion.Identity, Vector3.One);
            var drop = Vector3.Transform(new Vector3(0f, 0f, -_hiddenDrop), Quaternion.Conjugate(parentRotation)) / parentScale;
            locals[index] = locals[index] with { Translation = locals[index].Translation + drop };
        }

        (Quaternion Rotation, Vector3 Scale) World(int index)
        {
            if (worlds.TryGetValue(index, out var world))
            {
                return world;
            }

            var local = locals[index];
            (Quaternion Rotation, Vector3 Scale) parent = bones[index].ParentIndex >= 0 ? World(bones[index].ParentIndex) : (Quaternion.Identity, Vector3.One);
            world = (Quaternion.Normalize(parent.Rotation * local.Rotation), parent.Scale * local.Scale);
            worlds[index] = world;
            return world;
        }
    }
}
