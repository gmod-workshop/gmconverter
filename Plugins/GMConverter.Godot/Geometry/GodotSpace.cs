using System.Numerics;

namespace GMConverter.Godot.Geometry;

/// <summary>
/// Converts from Godot's space (right-handed, Y up, meters, models facing +Z as glTF has them) to
/// the SDK's (right-handed, Z up, X forward, Y left, meters). The change of basis is a rotation, so
/// handedness is kept; Godot draws clockwise triangles as front faces, so winding still flips.
/// </summary>
internal static class GodotSpace
{
    /// <summary>The way a Godot model faces once converted: glTF's forward, +Z, becomes +X.</summary>
    public static Vector3 Forward => Vector3.UnitX;

    public static Vector3 Position(Vector3 godot)
    {
        return new Vector3(godot.Z, godot.X, godot.Y);
    }

    public static Vector3 Direction(Vector3 godot)
    {
        return Position(godot);
    }

    public static Vector3 Scale(Vector3 godot)
    {
        return new Vector3(godot.Z, godot.X, godot.Y);
    }

    /// <summary>A rotation's axis maps like a position; a rotation keeps its angle.</summary>
    public static Quaternion Rotation(Quaternion godot)
    {
        return new Quaternion(godot.Z, godot.X, godot.Y, godot.W);
    }

    /// <summary>Godot puts V = 0 at the top of the image; the SDK at the bottom.</summary>
    public static Vector2 TextureCoordinate(Vector2 godot)
    {
        return new Vector2(godot.X, 1f - godot.Y);
    }
}
