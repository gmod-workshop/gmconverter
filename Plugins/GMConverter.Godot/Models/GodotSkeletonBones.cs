using System.Globalization;
using System.Numerics;
using GMConverter.Godot.Resources;
using GMConverter.Godot.Scenes;

namespace GMConverter.Godot.Models;

/// <summary>
/// The bones of a <c>Skeleton3D</c> node, read from its <c>bones/N/...</c> properties: each bone's
/// name, parent, rest transform and saved pose. Poses are relative to the parent bone; globals are
/// in the skeleton node's space.
/// </summary>
internal sealed class GodotSkeletonBones
{
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);

    public GodotSkeletonBones(GodotSceneNode skeleton)
    {
        Node = skeleton;
        var count = 0;
        while (skeleton[Key(count, "name")] is string)
        {
            count++;
        }

        Names = new string[count];
        Parents = new int[count];
        Poses = new Matrix4x4[count];
        Globals = new Matrix4x4[count];
        for (var bone = 0; bone < count; bone++)
        {
            Names[bone] = (string)skeleton[Key(bone, "name")]!;
            Parents[bone] = skeleton[Key(bone, "parent")] is long parent && parent < bone ? (int)parent : -1;
            Poses[bone] = Pose(skeleton, bone);
            Globals[bone] = Parents[bone] >= 0 ? Poses[bone] * Globals[Parents[bone]] : Poses[bone];
            _byName.TryAdd(Names[bone], bone);
        }
    }

    public GodotSceneNode Node { get; }

    public string[] Names { get; }

    /// <summary>Each bone's parent bone, or -1 for a bone directly under the skeleton node.</summary>
    public int[] Parents { get; }

    /// <summary>Each bone's transform relative to its parent, as the scene saved it.</summary>
    public Matrix4x4[] Poses { get; }

    /// <summary>Each bone's transform relative to the skeleton node.</summary>
    public Matrix4x4[] Globals { get; }

    public int Count => Names.Length;

    public int IndexOf(string name)
    {
        return _byName.GetValueOrDefault(name, -1);
    }

    // The saved pose, falling back to the rest transform for parts the scene did not save.
    private static Matrix4x4 Pose(GodotSceneNode skeleton, int bone)
    {
        var rest = skeleton[Key(bone, "rest")] is GodotTransform3D transform ? transform.ToMatrix() : Matrix4x4.Identity;
        Matrix4x4.Decompose(rest, out var restScale, out var restRotation, out var restPosition);
        var scale = skeleton[Key(bone, "scale")] is Vector3 savedScale ? savedScale : restScale;
        var rotation = skeleton[Key(bone, "rotation")] is Quaternion savedRotation ? savedRotation : restRotation;
        var position = skeleton[Key(bone, "position")] is Vector3 savedPosition ? savedPosition : restPosition;
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
    }

    private static string Key(int bone, string property)
    {
        return string.Create(CultureInfo.InvariantCulture, $"bones/{bone}/{property}");
    }
}
