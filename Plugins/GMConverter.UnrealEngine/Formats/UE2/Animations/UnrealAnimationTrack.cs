using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.UE2.Animations;

internal sealed record UnrealAnimationTrack(
    IReadOnlyList<Vector3> Positions,
    IReadOnlyList<Quaternion> Rotations,
    IReadOnlyList<float> Times)
{
    public (Vector3 Position, Quaternion Rotation) Sample(int frameIndex, int frameCount)
    {
        var frame = (float)frameIndex;
        var position = Positions.Count == 0
            ? Vector3.Zero
            : SampleVector(Positions, Times, frame, frameCount);
        var rotation = Rotations.Count == 0
            ? Quaternion.Identity
            : SampleQuaternion(Rotations, Times, frame, frameCount);

        return (position, rotation);
    }

    private static Vector3 SampleVector(IReadOnlyList<Vector3> keys, IReadOnlyList<float> times, float frame, int frameCount)
    {
        if (keys.Count == 1 || frameCount <= 1 || frame <= 0.0f)
        {
            return keys[0];
        }

        if (times.Count > 0 && (keys.Count == 1 || keys.Count == times.Count))
        {
            var (previous, next, fraction) = GetTimedKeyParameters(times, frame, frameCount);
            if (keys.Count == 1)
            {
                return keys[0];
            }

            return fraction > 0.0f ? Vector3.Lerp(keys[previous], keys[next], fraction) : keys[previous];
        }

        var (evenPrevious, evenNext, evenFraction) = GetEvenKeyParameters(keys.Count, frame, frameCount);
        return evenFraction > 0.0f ? Vector3.Lerp(keys[evenPrevious], keys[evenNext], evenFraction) : keys[evenPrevious];
    }

    private static Quaternion SampleQuaternion(IReadOnlyList<Quaternion> keys, IReadOnlyList<float> times, float frame, int frameCount)
    {
        if (keys.Count == 1 || frameCount <= 1 || frame <= 0.0f)
        {
            return Normalize(keys[0]);
        }

        if (times.Count > 0 && (keys.Count == 1 || keys.Count == times.Count))
        {
            var (previous, next, fraction) = GetTimedKeyParameters(times, frame, frameCount);
            if (keys.Count == 1)
            {
                return Normalize(keys[0]);
            }

            return fraction > 0.0f ? Normalize(Quaternion.Slerp(keys[previous], keys[next], fraction)) : Normalize(keys[previous]);
        }

        var (evenPrevious, evenNext, evenFraction) = GetEvenKeyParameters(keys.Count, frame, frameCount);
        return evenFraction > 0.0f ? Normalize(Quaternion.Slerp(keys[evenPrevious], keys[evenNext], evenFraction)) : Normalize(keys[evenPrevious]);
    }

    private static (int Previous, int Next, float Fraction) GetTimedKeyParameters(
        IReadOnlyList<float> times,
        float frame,
        int frameCount)
    {
        var previous = FindTimeKey(times, frame);
        var next = previous + 1;
        if (next >= times.Count)
        {
            return (previous, times.Count - 1, 0.0f);
        }

        var duration = times[next] - times[previous];
        var fraction = Math.Abs(duration) > 0.000001f ? (frame - times[previous]) / duration : 0.0f;
        return (previous, next, Math.Clamp(fraction, 0.0f, 1.0f));
    }

    private static (int Previous, int Next, float Fraction) GetEvenKeyParameters(int keyCount, float frame, int frameCount)
    {
        var position = frame / frameCount * keyCount;
        var previous = Math.Clamp((int)MathF.Floor(position), 0, keyCount - 1);
        var fraction = position - previous;
        var next = previous + 1;
        if (next >= keyCount)
        {
            next = keyCount - 1;
            fraction = 0.0f;
        }

        return (previous, next, Math.Clamp(fraction, 0.0f, 1.0f));
    }

    private static int FindTimeKey(IReadOnlyList<float> times, float frame)
    {
        var low = 0;
        var high = times.Count - 1;
        while (low + 4 < high)
        {
            var middle = (low + high) / 2;
            if (frame < times[middle])
            {
                high = middle - 1;
            }
            else
            {
                low = middle;
            }
        }

        for (var i = low; i <= high; i++)
        {
            if (Math.Abs(frame - times[i]) < 0.000001f)
            {
                return i;
            }

            if (frame < times[i])
            {
                return i > 0 ? i - 1 : 0;
            }
        }

        return high;
    }

    private static Quaternion Normalize(Quaternion value)
    {
        return value.LengthSquared() > 0.000001f ? Quaternion.Normalize(value) : Quaternion.Identity;
    }
}
