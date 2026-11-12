using GMConverter.Godot.Scenes;

namespace GMConverter.Godot.Models;

/// <summary>
/// What a bone of the converted skeleton stands for: a scene node (<see cref="SkeletonBone"/> -1)
/// or a bone of the <c>Skeleton3D</c> node <see cref="Node"/>.
/// </summary>
internal readonly record struct GodotBoneKey(GodotSceneNode Node, int SkeletonBone = -1);
