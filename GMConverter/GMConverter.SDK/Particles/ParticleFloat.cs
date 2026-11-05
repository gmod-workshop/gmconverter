namespace GMConverter.SDK.Particles;

/// <summary>
/// A number a particle property takes: a constant, a random range, a curve, or a random range
/// between two curves. Each particle picks a point between <see cref="Min"/> and <see cref="Max"/>
/// when it spawns and follows it along the curves; keys are linearly interpolated and held past the ends.
/// </summary>
/// <param name="Min">The low curve; a single key is a constant.</param>
/// <param name="Max">The high curve; the same keys as <paramref name="Min"/> when the value isn't random.</param>
public sealed record ParticleFloat(IReadOnlyList<ParticleKey> Min, IReadOnlyList<ParticleKey> Max)
{
    /// <summary>The same value always.</summary>
    public static ParticleFloat Constant(float value) => new([new ParticleKey(0f, value)], [new ParticleKey(0f, value)]);

    /// <summary>A value picked between <paramref name="min"/> and <paramref name="max"/> for each particle.</summary>
    public static ParticleFloat Range(float min, float max) => new([new ParticleKey(0f, min)], [new ParticleKey(0f, max)]);

    /// <summary>A value that follows <paramref name="keys"/>.</summary>
    public static ParticleFloat Curve(IReadOnlyList<ParticleKey> keys) => new(keys, keys);

    /// <summary>Whether particles can take different values.</summary>
    public bool IsRandom => !Min.SequenceEqual(Max);

    /// <summary>Whether the value changes along the curve.</summary>
    public bool IsAnimated => Distinct(Min) || Distinct(Max);

    /// <summary>The value at <paramref name="time"/> for a particle that picked <paramref name="random"/> (0 = <see cref="Min"/>, 1 = <see cref="Max"/>).</summary>
    public float Evaluate(float time, float random = 0f)
    {
        var low = Sample(Min, time);
        return low + ((Sample(Max, time) - low) * random);
    }

    /// <summary>The smallest and largest values the property can take anywhere on its curves.</summary>
    public (float Min, float Max) Bounds()
    {
        var values = Min.Concat(Max).Select(key => key.Value).ToArray();
        return values.Length == 0 ? (0f, 0f) : (values.Min(), values.Max());
    }

    private static bool Distinct(IReadOnlyList<ParticleKey> keys) => keys.Any(key => key.Value != keys[0].Value);

    private static float Sample(IReadOnlyList<ParticleKey> keys, float time)
    {
        if (keys.Count == 0)
        {
            return 0f;
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
                return span > 0f ? a.Value + ((b.Value - a.Value) * (time - a.Time) / span) : b.Value;
            }
        }

        return keys[^1].Value;
    }
}
