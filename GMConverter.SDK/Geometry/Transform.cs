using System.Numerics;

namespace GMConverter.SDK.Geometry;

public sealed record Transform(Vector3 Translation, Quaternion Rotation, Vector3 Scale)
{
    public static Transform Identity { get; } = new(Vector3.Zero, Quaternion.Identity, Vector3.One);
}
