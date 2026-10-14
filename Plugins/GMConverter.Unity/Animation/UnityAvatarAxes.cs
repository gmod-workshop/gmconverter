using System.Numerics;
using AssetsTools.NET;
using GMConverter.Unity.Geometry;

namespace GMConverter.Unity.Animation;

/// <summary>
/// The frame a human bone's muscles turn it in. With the angles each muscle gives, the bone's
/// local rotation is <c>PreQ * SwingTwist(Sign * angles) * PostQ⁻¹</c>, where x is the twist about
/// the bone and y and z swing it. <see cref="Min"/> and <see cref="Max"/> are the angles, in
/// radians, that muscle values of -1 and 1 reach.
/// </summary>
internal sealed record UnityAvatarAxes(Quaternion PreQ, Quaternion PostQ, Vector3 Sign, Vector3 Min, Vector3 Max)
{
    public static UnityAvatarAxes Read(AssetTypeValueField axes)
    {
        var limit = axes["m_Limit"];
        return new UnityAvatarAxes(
            Quaternion.Normalize(UnitySpace.ReadQuaternion(axes["m_PreQ"])),
            Quaternion.Normalize(UnitySpace.ReadQuaternion(axes["m_PostQ"])),
            UnitySpace.ReadVector3(axes["m_Sgn"]),
            UnitySpace.ReadVector3(limit["m_Min"]),
            UnitySpace.ReadVector3(limit["m_Max"]));
    }

    /// <summary>The local rotation for muscle values <paramref name="muscles"/> (x twist, y and z swing).</summary>
    public Quaternion Rotate(Vector3 muscles)
    {
        var angles = new Vector3(Angle(muscles.X, Min.X, Max.X), Angle(muscles.Y, Min.Y, Max.Y), Angle(muscles.Z, Min.Z, Max.Z)) * Sign;
        return Quaternion.Normalize(PreQ * SwingTwist(angles) * Quaternion.Conjugate(PostQ));
    }

    /// <summary>A swing about the axis (0, y, z) by its length, after a twist of x about X; all in radians.</summary>
    public static Quaternion SwingTwist(Vector3 angles)
    {
        var swingAngle = MathF.Sqrt((angles.Y * angles.Y) + (angles.Z * angles.Z));
        var swing = swingAngle > 1e-8f
            ? Quaternion.CreateFromAxisAngle(new Vector3(0f, angles.Y / swingAngle, angles.Z / swingAngle), swingAngle)
            : Quaternion.Identity;
        return swing * Quaternion.CreateFromAxisAngle(Vector3.UnitX, angles.X);
    }

    private static float Angle(float muscle, float min, float max)
    {
        return muscle >= 0f ? muscle * max : -muscle * min;
    }
}
