using System.Numerics;

namespace GMConverter.SDK.Particles;

/// <summary>
/// A vector a particle property takes (a size, a colour, a velocity): a constant, a random box, a
/// curve, or a random box between two curves. Each particle picks a point between <see cref="Min"/>
/// and <see cref="Max"/> per component when it spawns, then follows the curves.
/// </summary>
/// <param name="Min">The low curve; a single key is a constant.</param>
/// <param name="Max">The high curve; the same keys as <paramref name="Min"/> when the value isn't random.</param>
public sealed record ParticleVector(IReadOnlyList<ParticleVectorKey> Min, IReadOnlyList<ParticleVectorKey> Max)
{
    /// <summary>The same value always.</summary>
    public static ParticleVector Constant(Vector3 value) => new([new ParticleVectorKey(0f, value)], [new ParticleVectorKey(0f, value)]);

    /// <summary>A value picked inside the box from <paramref name="min"/> to <paramref name="max"/> for each particle.</summary>
    public static ParticleVector Range(Vector3 min, Vector3 max) => new([new ParticleVectorKey(0f, min)], [new ParticleVectorKey(0f, max)]);

    /// <summary>A value that follows <paramref name="keys"/>.</summary>
    public static ParticleVector Curve(IReadOnlyList<ParticleVectorKey> keys) => new(keys, keys);

    /// <summary>Whether particles can take different values.</summary>
    public bool IsRandom => !Min.SequenceEqual(Max);

    /// <summary>Whether the value changes along the curve.</summary>
    public bool IsAnimated => Distinct(Min) || Distinct(Max);

    /// <summary>The value at <paramref name="time"/> for a particle that picked <paramref name="random"/> per component.</summary>
    public Vector3 Evaluate(float time, Vector3 random = default)
    {
        var low = Sample(Min, time);
        return low + ((Sample(Max, time) - low) * random);
    }

    /// <summary>One component as a <see cref="ParticleFloat"/>.</summary>
    public ParticleFloat Component(int index) => new(
        [.. Min.Select(key => new ParticleKey(key.Time, key.Value[index]))],
        [.. Max.Select(key => new ParticleKey(key.Time, key.Value[index]))]);

    private static bool Distinct(IReadOnlyList<ParticleVectorKey> keys) => keys.Any(key => key.Value != keys[0].Value);

    private static Vector3 Sample(IReadOnlyList<ParticleVectorKey> keys, float time)
    {
        if (keys.Count == 0)
        {
            return Vector3.Zero;
        }

        if (time <= keys[0].Time)
        {
            return keys[0].Value;
        }

        for (var index = 1; index < keys.Count; index++)
        {
            if (time <= keys[index].Time)
            {
                var (a, b) = (keys[index - 1], keys[index]);
                var span = b.Time - a.Time;
                return span > 0f ? Vector3.Lerp(a.Value, b.Value, (time - a.Time) / span) : b.Value;
            }
        }

        return keys[^1].Value;
    }
}
