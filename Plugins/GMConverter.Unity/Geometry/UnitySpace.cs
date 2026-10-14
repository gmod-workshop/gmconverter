using System.Numerics;
using AssetsTools.NET;

namespace GMConverter.Unity.Geometry;

/// <summary>
/// Converts from Unity's space (left-handed, Y up, Z forward, X right, meters) to the SDK's
/// (right-handed, Z up, X forward, Y left, meters). The change of basis is a mirror, so triangle
/// winding flips with it.
/// </summary>
internal static class UnitySpace
{
    /// <summary>The way a Unity model faces once converted: Unity's forward, +Z, becomes +X.</summary>
    public static Vector3 Forward => Vector3.UnitX;

    public static Vector3 Position(Vector3 unity)
    {
        return new Vector3(unity.Z, -unity.X, unity.Y);
    }

    public static Vector3 Direction(Vector3 unity)
    {
        return Position(unity);
    }

    public static Vector3 Scale(Vector3 unity)
    {
        return new Vector3(unity.Z, unity.X, unity.Y);
    }

    /// <summary>
    /// A rotation conjugated by the mirror: the axis maps like a position and, because the mirror
    /// flips handedness, the angle changes sign.
    /// </summary>
    public static Quaternion Rotation(Quaternion unity)
    {
        return new Quaternion(-unity.Z, unity.X, -unity.Y, unity.W);
    }

    /// <summary>
    /// Unity texture coordinates already put V = 0 at the bottom of the image, as the SDK does; the
    /// texture reader turns the image rows the right way up instead.
    /// </summary>
    public static Vector2 TextureCoordinate(Vector2 unity)
    {
        return unity;
    }

    public static Vector3 ReadVector3(AssetTypeValueField field)
    {
        return new Vector3(field["x"].AsFloat, field["y"].AsFloat, field["z"].AsFloat);
    }

    public static Quaternion ReadQuaternion(AssetTypeValueField field)
    {
        return new Quaternion(field["x"].AsFloat, field["y"].AsFloat, field["z"].AsFloat, field["w"].AsFloat);
    }

    /// <summary>
    /// Reads a Unity <c>Matrix4x4f</c>. Unity stores column-vector matrices (<c>eRC</c> is row R,
    /// column C); System.Numerics multiplies row vectors, so the result is the transpose.
    /// </summary>
    public static Matrix4x4 ReadMatrix(AssetTypeValueField field)
    {
        return new Matrix4x4(
            field["e00"].AsFloat, field["e10"].AsFloat, field["e20"].AsFloat, field["e30"].AsFloat,
            field["e01"].AsFloat, field["e11"].AsFloat, field["e21"].AsFloat, field["e31"].AsFloat,
            field["e02"].AsFloat, field["e12"].AsFloat, field["e22"].AsFloat, field["e32"].AsFloat,
            field["e03"].AsFloat, field["e13"].AsFloat, field["e23"].AsFloat, field["e33"].AsFloat);
    }

    /// <summary>A local transform as a System.Numerics matrix (scale, then rotate, then translate).</summary>
    public static Matrix4x4 Compose(Vector3 translation, Quaternion rotation, Vector3 scale)
    {
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
    }
}
