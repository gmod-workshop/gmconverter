using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.UE2.Animations;

internal readonly record struct UnrealPackedShortVector(short X, short Y, short Z)
{
    public Vector3 ToVector(float scale)
    {
        var factor = scale / 32767.0f;
        return new Vector3(X * factor, Y * factor, Z * factor);
    }

    public Quaternion ToOldQuaternion()
    {
        const float scale = 0.000095876726845745f;
        var x = X * scale;
        var y = Y * scale;
        var z = Z * scale;
        var length = MathF.Sqrt(x * x + y * y + z * z);
        if (length > 0.0f)
        {
            var factor = MathF.Sin(length / 2.0f) / length;
            x *= factor;
            y *= factor;
            z *= factor;
        }

        var wSquared = 1.0f - (x * x + y * y + z * z);
        return new Quaternion(x, y, z, wSquared > 0.0f ? MathF.Sqrt(wSquared) : 0.0f);
    }

    public Quaternion ToQuaternion()
    {
        const float scale = 0.70710678118f / 32767.0f;
        var a = unchecked((short)(X & 0xFFFE)) * scale;
        var b = unchecked((short)(Y & 0xFFFE)) * scale;
        var c = unchecked((short)(Z & 0xFFFE)) * scale;
        var dSquared = 1.0f - (a * a + b * b + c * c);
        var d = dSquared > 0.0f ? MathF.Sqrt(dSquared) : 0.0f;
        if ((Z & 1) != 0)
        {
            d = -d;
        }

        if ((Y & 1) != 0)
        {
            return (X & 1) != 0
                ? new Quaternion(d, a, b, c)
                : new Quaternion(c, d, a, b);
        }

        return (X & 1) != 0
            ? new Quaternion(b, c, d, a)
            : new Quaternion(a, b, c, d);
    }
}
