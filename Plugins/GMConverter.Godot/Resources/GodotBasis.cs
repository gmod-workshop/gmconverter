using System.Numerics;

namespace GMConverter.Godot.Resources;

/// <summary>A Godot <c>Basis</c> (a 3×3 matrix), saved as its rows.</summary>
internal readonly record struct GodotBasis(Vector3 Row0, Vector3 Row1, Vector3 Row2);
