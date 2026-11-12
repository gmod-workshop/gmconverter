using System.Numerics;

namespace GMConverter.Godot.Resources;

/// <summary>
/// A Godot <c>Transform3D</c>: a basis, saved as its rows, and an origin. The basis's columns are
/// the transformed X, Y and Z axes.
/// </summary>
internal readonly record struct GodotTransform3D(Vector3 Row0, Vector3 Row1, Vector3 Row2, Vector3 Origin)
{
    public static GodotTransform3D Identity { get; } = new(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.Zero);

    /// <summary>The transform as a <c>System.Numerics</c> matrix, which transforms row vectors.</summary>
    public Matrix4x4 ToMatrix()
    {
        return new Matrix4x4(
            Row0.X, Row1.X, Row2.X, 0f,
            Row0.Y, Row1.Y, Row2.Y, 0f,
            Row0.Z, Row1.Z, Row2.Z, 0f,
            Origin.X, Origin.Y, Origin.Z, 1f);
    }
}
