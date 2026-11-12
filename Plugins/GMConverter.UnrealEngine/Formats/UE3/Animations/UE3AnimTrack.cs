using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.UE3.Animations;

/// <summary>
/// One bone's keys in a sequence. Key times are in frames; null times mean the keys are spread
/// evenly over the sequence. An empty key list means the track leaves that part to the reference pose.
/// </summary>
internal sealed record UE3AnimTrack(
    IReadOnlyList<Vector3> Positions,
    IReadOnlyList<float>? PositionTimes,
    IReadOnlyList<Quaternion> Rotations,
    IReadOnlyList<float>? RotationTimes)
{
    public Vector3? SamplePosition(float frame, int frameCount)
    {
        return Positions.Count == 0 ? null : Sample(Positions, PositionTimes, frame, frameCount, Vector3.Lerp);
    }

    public Quaternion? SampleRotation(float frame, int frameCount)
    {
        return Rotations.Count == 0 ? null : Quaternion.Normalize(Sample(Rotations, RotationTimes, frame, frameCount, Quaternion.Slerp));
    }

    private static T Sample<T>(IReadOnlyList<T> keys, IReadOnlyList<float>? times, float frame, int frameCount, Func<T, T, float, T> lerp)
    {
        if (keys.Count == 1)
        {
            return keys[0];
        }

        if (times is null || times.Count != keys.Count)
        {
            var position = frameCount > 1 ? frame * (keys.Count - 1) / (frameCount - 1) : 0;
            var index = Math.Clamp((int)MathF.Floor(position), 0, keys.Count - 1);
            var next = Math.Min(index + 1, keys.Count - 1);
            return lerp(keys[index], keys[next], Math.Clamp(position - index, 0, 1));
        }

        if (frame <= times[0])
        {
            return keys[0];
        }

        for (var i = 1; i < times.Count; i++)
        {
            if (frame <= times[i])
            {
                var span = times[i] - times[i - 1];
                return lerp(keys[i - 1], keys[i], span > 0 ? (frame - times[i - 1]) / span : 0);
            }
        }

        return keys[^1];
    }
}
