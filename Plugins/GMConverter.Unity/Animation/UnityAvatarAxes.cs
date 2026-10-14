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

    /// <summary>The twist angle, in radians, that the twist muscle value <paramref name="muscle"/> turns the bone by.</summary>
    public float Twist(float muscle)
    {
        return Angle(muscle, Min.X, Max.X) * Sign.X;
    }

    /// <summary>A turn of <paramref name="angle"/> radians about the twist axis, in the bone's local frame.</summary>
    public Quaternion TwistBy(float angle)
    {
        return Quaternion.Normalize(PostQ * Quaternion.CreateFromAxisAngle(Vector3.UnitX, angle) * Quaternion.Conjugate(PostQ));
    }

    /// <summary>
    /// Mecanim's swing-twist from angles in radians: a twist of x about X, then a swing toward y
    /// and z. Each angle is held as the tangent of its half, so the swing turns about (0, tan y/2,
    /// tan z/2) and the quaternion is (tx, ty + tx·tz, tz − tx·ty, 1) normalized. Swinging on both
    /// axes at once lands short of a plain axis-angle swing near the limits.
    /// </summary>
    public static Quaternion SwingTwist(Vector3 angles)
    {
        var t = new Vector3(MathF.Tan(angles.X * 0.5f), MathF.Tan(angles.Y * 0.5f), MathF.Tan(angles.Z * 0.5f));
        return Quaternion.Normalize(new Quaternion(t.X, t.Y + (t.X * t.Z), t.Z - (t.X * t.Y), 1f));
    }

    private static float Angle(float muscle, float min, float max)
    {
        return muscle >= 0f ? muscle * max : -muscle * min;
    }
}
