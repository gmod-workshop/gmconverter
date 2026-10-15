using System.Numerics;
using GMConverter.Godot.Geometry;
using GMConverter.Godot.Models;
using GMConverter.Godot.Resources;
using GMConverter.Godot.Scenes;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Geometry;

namespace GMConverter.Godot.Animation;

/// <summary>
/// Samples Godot animations onto a converted skeleton. Transform tracks move <c>Skeleton3D</c>
/// bones and scene nodes; value tracks move nodes through their <c>position</c>,
/// <c>rotation</c>, <c>quaternion</c>, <c>scale</c> or <c>transform</c>, and show or hide them
/// through <c>visible</c>. Whatever a clip leaves alone stays as the scene saved it.
/// </summary>
internal sealed class GodotAnimationBuilder(GodotSkeleton skeleton, Func<GodotSceneNode, GodotSkeletonBones> skeletonBones)
{
    private const float _sampleRate = 30f;
    private const long _defaultRotationOrder = 2; // YXZ

    /// <summary>Whether a track moves its target (rather than showing or hiding it).</summary>
    public static bool Moves(GodotAnimationTrack track)
    {
        return track.Kind != GodotAnimationTrackKind.Value ||
            track.SubName is "position" or "rotation" or "quaternion" or "scale" or "transform";
    }

    /// <summary>Whether a track shows or hides its target.</summary>
    public static bool Toggles(GodotAnimationTrack track)
    {
        return track.Kind == GodotAnimationTrackKind.Value && track.SubName == "visible";
    }

    /// <summary>The node or <c>Skeleton3D</c> bone a track moves or toggles, or null when it is neither or is not in the scene.</summary>
    public static GodotBoneKey? Target(GodotPlayedAnimation played, GodotAnimationTrack track, Func<GodotSceneNode, GodotSkeletonBones> skeletonBones)
    {
        if ((!Moves(track) && !Toggles(track)) || played.Root.Find(track.Path.Names) is not { } node)
        {
            return null;
        }

        if (track.Kind == GodotAnimationTrackKind.Value || track.SubName is not { } boneName)
        {
            return new GodotBoneKey(node);
        }

        if (node.Type != "Skeleton3D")
        {
            return null;
        }

        var bone = skeletonBones(node).IndexOf(boneName);
        return bone >= 0 ? new GodotBoneKey(node, bone) : null;
    }

    /// <summary>Samples <paramref name="played"/>, or returns null when nothing in it moves or toggles this skeleton.</summary>
    public AnimationClip? Build(GodotPlayedAnimation played, string name)
    {
        var tracks = played.Animation.Tracks
            .Select(track => (Track: track, Bone: Target(played, track, skeletonBones) is { } key && skeleton.Indices.TryGetValue(key, out var bone) ? bone : -1))
            .Where(entry => entry.Bone >= 0)
            .GroupBy(entry => entry.Bone, entry => entry.Track)
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (tracks.Count == 0)
        {
            return null;
        }

        var bones = skeleton.Skeleton.Bones;
        var length = Math.Max(played.Animation.Length, 0f);
        var frameCount = Math.Max(1, (int)MathF.Round(length * _sampleRate)) + 1;
        var keyframes = bones.Select(_ => new List<TransformKeyframe>(frameCount)).ToArray();
        var visibility = tracks
            .Where(pair => pair.Value.Any(Toggles))
            .ToDictionary(pair => pair.Key, _ => new List<VisibilityKeyframe>());
        var rests = skeleton.Locals.Select(Decompose).ToArray();
        var nodes = skeleton.Indices.ToDictionary(pair => pair.Value, pair => pair.Key);

        for (var frame = 0; frame < frameCount; frame++)
        {
            var time = Math.Min(frame / _sampleRate, length);
            for (var bone = 0; bone < bones.Count; bone++)
            {
                if (!tracks.TryGetValue(bone, out var boneTracks))
                {
                    keyframes[bone].Add(new TransformKeyframe(time, bones[bone].LocalBindPose));
                    continue;
                }

                var (position, rotation, scale) = rests[bone];
                foreach (var track in boneTracks)
                {
                    Apply(track, track.Sample(time), nodes[bone].Node, ref position, ref rotation, ref scale);
                }

                keyframes[bone].Add(new TransformKeyframe(
                    time,
                    new Transform(GodotSpace.Position(position), GodotSpace.Rotation(Quaternion.Normalize(rotation)), GodotSpace.Scale(scale))));

                if (visibility.TryGetValue(bone, out var frames) &&
                    boneTracks.LastOrDefault(Toggles)?.Sample(time) is bool visible &&
                    (frames.Count == 0 || frames[^1].Visible != visible))
                {
                    frames.Add(new VisibilityKeyframe(time, visible));
                }
            }
        }

        IAnimationTrack[] sdkTracks =
        [
            .. keyframes.Select((frames, bone) => new BoneTransformTrack(bone, frames)),
            .. visibility.Select(pair => new BoneVisibilityTrack(pair.Key, pair.Value)),
        ];
        return new AnimationClip(name, _sampleRate, (frameCount - 1) / _sampleRate, sdkTracks, Loops: played.Animation.Loops);
    }

    private static void Apply(GodotAnimationTrack track, object? value, GodotSceneNode node, ref Vector3 position, ref Quaternion rotation, ref Vector3 scale)
    {
        // A value track names the property; a transform track's kind is the property.
        var property = track.Kind switch
        {
            GodotAnimationTrackKind.Position => "position",
            GodotAnimationTrackKind.Rotation => "quaternion",
            GodotAnimationTrackKind.Scale => "scale",
            _ => track.SubName,
        };

        switch (property, value)
        {
            case ("position", Vector3 vector):
                position = vector;
                break;
            case ("quaternion", Quaternion quaternion):
                rotation = quaternion;
                break;
            case ("scale", Vector3 vector):
                scale = vector;
                break;
            case ("rotation", Vector3 angles):
                rotation = FromEuler(angles, node["rotation_order"] is long order ? order : _defaultRotationOrder);
                break;
            case ("transform", GodotTransform3D transform):
                (position, rotation, scale) = Decompose(transform.ToMatrix());
                break;
        }
    }

    // Godot's Basis::from_euler: the rotations about each axis, composed in the order's letters
    // (YXZ, the default, turns about Z first, then X, then Y).
    private static Quaternion FromEuler(Vector3 angles, long order)
    {
        var x = Quaternion.CreateFromAxisAngle(Vector3.UnitX, angles.X);
        var y = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angles.Y);
        var z = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angles.Z);
        return order switch
        {
            0 => x * y * z,
            1 => x * z * y,
            3 => y * z * x,
            4 => z * x * y,
            5 => z * y * x,
            _ => y * x * z,
        };
    }

    private static (Vector3 Position, Quaternion Rotation, Vector3 Scale) Decompose(Matrix4x4 local)
    {
        Matrix4x4.Decompose(local, out var scale, out var rotation, out var position);
        return (position, rotation, scale);
    }
}
