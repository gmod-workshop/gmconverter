using System.Numerics;

namespace GMConverter.Godot.Resources;

/// <summary>A Godot axis-aligned box: its lowest corner and its size.</summary>
internal readonly record struct GodotAabb(Vector3 Position, Vector3 Size);
