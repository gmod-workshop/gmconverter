using GMConverter.Godot.Scenes;

namespace GMConverter.Godot.Animation;

/// <summary>An animation an <c>AnimationPlayer</c> can play, with the node its track paths start from.</summary>
internal sealed record GodotPlayedAnimation(GodotSceneNode Player, GodotSceneNode Root, GodotAnimation Animation);
