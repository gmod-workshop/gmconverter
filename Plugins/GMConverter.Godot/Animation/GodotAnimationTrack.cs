using System.Globalization;
using System.Numerics;
using GMConverter.Godot.Resources;

namespace GMConverter.Godot.Animation;

/// <summary>
/// One track of a Godot <c>Animation</c>: what it moves (a node, a <c>Skeleton3D</c> bone or a
/// node property) and its keys. Position, rotation and scale tracks key a node's or bone's
/// transform; value tracks key a property such as <c>position</c> or <c>visible</c>.
/// </summary>
internal sealed class GodotAnimationTrack
{
    private readonly float[] _times;
    private readonly float[] _transitions;
    private readonly object?[] _values;
    private readonly GodotInterpolation _interpolation;

    private GodotAnimationTrack(GodotAnimationTrackKind kind, GodotNodePath path, float[] times, float[] transitions, object?[] values, GodotInterpolation interpolation)
    {
        Kind = kind;
        Path = path;
        _times = times;
        _transitions = transitions;
        _values = values;
        _interpolation = interpolation;
    }

    public GodotAnimationTrackKind Kind { get; }

    /// <summary>
    /// The node the track moves, relative to the player's root node; the sub-name is the bone of
    /// a transform track on a <c>Skeleton3D</c>, or the property of a value track.
    /// </summary>
    public GodotNodePath Path { get; }

    /// <summary>The track's property (value tracks) or bone (transform tracks), or null.</summary>
    public string? SubName => Path.SubNames.Count > 0 ? string.Join(':', Path.SubNames) : null;

    /// <summary>
    /// Reads track <paramref name="index"/> of an <c>Animation</c>, or returns null when the
    /// track is disabled, empty or of a kind that cannot move a model (method, audio, bezier).
    /// </summary>
    public static GodotAnimationTrack? Read(GodotResource animation, int index)
    {
        string Key(string property) => string.Create(CultureInfo.InvariantCulture, $"tracks/{index}/{property}");

        if (animation[Key("enabled")] is false || animation[Key("path")] is not GodotNodePath path)
        {
            return null;
        }

        var interpolation = animation[Key("interp")] is long interp ? (GodotInterpolation)interp : GodotInterpolation.Linear;
        var kind = animation[Key("type")] switch
        {
            "position_3d" => GodotAnimationTrackKind.Position,
            "rotation_3d" => GodotAnimationTrackKind.Rotation,
            "scale_3d" => GodotAnimationTrackKind.Scale,
            "value" => GodotAnimationTrackKind.Value,
            _ => (GodotAnimationTrackKind?)null,
        };

        return kind switch
        {
            GodotAnimationTrackKind.Value => ReadValueTrack(path, animation[Key("keys")], interpolation),
            { } transformKind => ReadTransformTrack(transformKind, path, animation[Key("keys")], interpolation),
            null => null,
        };
    }

    /// <summary>The track's value at <paramref name="time"/>, between the keys around it.</summary>
    public object? Sample(float time)
    {
        var next = Array.FindIndex(_times, keyTime => keyTime > time);
        if (next == 0)
        {
            return _values[0];
        }

        if (next < 0)
        {
            return _values[^1];
        }

        var previous = next - 1;
        if (_interpolation == GodotInterpolation.Nearest)
        {
            return _values[previous];
        }

        var span = _times[next] - _times[previous];
        var weight = Ease(span > 0f ? (time - _times[previous]) / span : 1f, _transitions[previous]);
        var from = _values[previous];
        var to = _values[next];
        if (_interpolation is GodotInterpolation.Cubic or GodotInterpolation.CubicAngle)
        {
            var before = _values[Math.Max(previous - 1, 0)];
            var after = _values[Math.Min(next + 1, _values.Length - 1)];
            return Cubic(before, from, to, after, weight) ?? Interpolate(from, to, weight);
        }

        if (_interpolation == GodotInterpolation.LinearAngle && from is Vector3 fromAngles && to is Vector3 toAngles)
        {
            return new Vector3(LerpAngle(fromAngles.X, toAngles.X, weight), LerpAngle(fromAngles.Y, toAngles.Y, weight), LerpAngle(fromAngles.Z, toAngles.Z, weight));
        }

        return Interpolate(from, to, weight);
    }

    // Transform tracks pack each key as time, transition, then three (position, scale) or four
    // (rotation) floats.
    private static GodotAnimationTrack? ReadTransformTrack(GodotAnimationTrackKind kind, GodotNodePath path, object? keys, GodotInterpolation interpolation)
    {
        var stride = kind == GodotAnimationTrackKind.Rotation ? 6 : 5;
        if (keys is not float[] data || data.Length < stride)
        {
            return null;
        }

        var count = data.Length / stride;
        var times = new float[count];
        var transitions = new float[count];
        var values = new object?[count];
        for (var key = 0; key < count; key++)
        {
            var offset = key * stride;
            times[key] = data[offset];
            transitions[key] = data[offset + 1];
            values[key] = kind == GodotAnimationTrackKind.Rotation
                ? new Quaternion(data[offset + 2], data[offset + 3], data[offset + 4], data[offset + 5])
                : new Vector3(data[offset + 2], data[offset + 3], data[offset + 4]);
        }

        return new GodotAnimationTrack(kind, path, times, transitions, values, interpolation);
    }

    // Value tracks keep times, transitions and values apart. Discrete tracks (update mode 1) hold
    // each value until the next key, whatever the interpolation says.
    private static GodotAnimationTrack? ReadValueTrack(GodotNodePath path, object? keys, GodotInterpolation interpolation)
    {
        if (keys is not Dictionary<object, object?> data ||
            data.GetValueOrDefault("times") is not float[] times ||
            data.GetValueOrDefault("values") is not List<object?> values ||
            times.Length == 0 ||
            values.Count != times.Length)
        {
            return null;
        }

        var transitions = data.GetValueOrDefault("transitions") is float[] saved && saved.Length == times.Length
            ? saved
            : [.. times.Select(_ => 1f)];
        if (data.GetValueOrDefault("update") is 1L)
        {
            interpolation = GodotInterpolation.Nearest;
        }

        return new GodotAnimationTrack(GodotAnimationTrackKind.Value, path, times, transitions, [.. values], interpolation);
    }

    private static object? Interpolate(object? from, object? to, float weight)
    {
        return (from, to) switch
        {
            (Vector3 a, Vector3 b) => Vector3.Lerp(a, b, weight),
            (Quaternion a, Quaternion b) => Quaternion.Slerp(a, b, weight),
            (double a, double b) => a + ((b - a) * weight),
            _ => from, // booleans and anything else switch at the next key
        };
    }

    private static object? Cubic(object? before, object? from, object? to, object? after, float weight)
    {
        return (before, from, to, after) switch
        {
            (Vector3 a, Vector3 b, Vector3 c, Vector3 d) => new Vector3(
                CubicInterpolate(a.X, b.X, c.X, d.X, weight),
                CubicInterpolate(a.Y, b.Y, c.Y, d.Y, weight),
                CubicInterpolate(a.Z, b.Z, c.Z, d.Z, weight)),
            (double a, double b, double c, double d) => (double)CubicInterpolate((float)a, (float)b, (float)c, (float)d, weight),
            _ => null,
        };
    }

    // Godot's Math::cubic_interpolate: a Catmull-Rom spline through the keys.
    private static float CubicInterpolate(float before, float from, float to, float after, float weight)
    {
        return 0.5f * (
            (from * 2f) +
            ((-before + to) * weight) +
            (((2f * before) - (5f * from) + (4f * to) - after) * weight * weight) +
            ((-before + (3f * from) - (3f * to) + after) * weight * weight * weight));
    }

    // Godot's Math::lerp_angle: the shorter way round.
    private static float LerpAngle(float from, float to, float weight)
    {
        var difference = (to - from) % MathF.Tau;
        var shortest = ((2f * difference) % MathF.Tau) - difference;
        return from + (shortest * weight);
    }

    // Godot's Math::ease, which shapes the step from a key to the next by the key's transition:
    // 1 is linear, above 1 eases in, between 0 and 1 eases out, below 0 eases in and out, and 0
    // holds the key.
    private static float Ease(float weight, float transition)
    {
        weight = Math.Clamp(weight, 0f, 1f);
        if (transition == 1f)
        {
            return weight;
        }

        if (transition > 0f)
        {
            return transition < 1f ? 1f - MathF.Pow(1f - weight, 1f / transition) : MathF.Pow(weight, transition);
        }

        if (transition < 0f)
        {
            return weight < 0.5f
                ? MathF.Pow(weight * 2f, -transition) * 0.5f
                : ((1f - MathF.Pow(1f - ((weight - 0.5f) * 2f), -transition)) * 0.5f) + 0.5f;
        }

        return 0f;
    }
}
