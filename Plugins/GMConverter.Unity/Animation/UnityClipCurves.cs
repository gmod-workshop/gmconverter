using AssetsTools.NET;
using GMConverter.Unity.Assets;

namespace GMConverter.Unity.Animation;

/// <summary>
/// The curves of a Mecanim clip (<c>m_MuscleClip.m_Clip</c>), which Unity keeps in three blocks:
/// streamed cubic keys, dense samples at a fixed rate, and constants. Curve indices run through
/// the blocks in that order, matching the clip's bindings; since Unity 2022 the streamed block
/// ends with discrete curves (integers and object references), stepped from key to key.
/// </summary>
internal sealed class UnityClipCurves
{
    private readonly StreamedKey[][] _streamed;
    private readonly float[] _dense;
    private readonly int _denseCurveCount;
    private readonly int _denseFrameCount;
    private readonly float _denseSampleRate;
    private readonly float _denseBeginTime;
    private readonly float[] _constant;

    private UnityClipCurves(StreamedKey[][] streamed, float[] dense, int denseCurveCount, int denseFrameCount, float denseSampleRate, float denseBeginTime, float[] constant)
    {
        _streamed = streamed;
        _dense = dense;
        _denseCurveCount = denseCurveCount;
        _denseFrameCount = denseFrameCount;
        _denseSampleRate = denseSampleRate;
        _denseBeginTime = denseBeginTime;
        _constant = constant;
    }

    public int CurveCount => _streamed.Length + _denseCurveCount + _constant.Length;

    public static UnityClipCurves Read(AssetTypeValueField clip)
    {
        var streamedClip = clip["m_StreamedClip"];
        var discrete = streamedClip["discreteCurveCount"] is { IsDummy: false } count ? count.AsInt : 0;
        var streamed = ReadStreamed(streamedClip["data"], streamedClip["curveCount"].AsInt + discrete);

        var denseClip = clip["m_DenseClip"];
        var dense = ReadFloats(denseClip["m_SampleArray"]);
        var constant = ReadFloats(clip["m_ConstantClip"]["data"]);

        return new UnityClipCurves(
            streamed,
            dense,
            (int)denseClip["m_CurveCount"].AsUInt,
            denseClip["m_FrameCount"].AsInt,
            denseClip["m_SampleRate"].AsFloat,
            denseClip["m_BeginTime"].AsFloat,
            constant);
    }

    /// <summary>Writes every curve's value at <paramref name="time"/> into <paramref name="values"/>.</summary>
    public void Sample(float time, Span<float> values)
    {
        for (var curve = 0; curve < _streamed.Length; curve++)
        {
            values[curve] = SampleStreamed(_streamed[curve], time);
        }

        var offset = _streamed.Length;
        if (_denseCurveCount > 0 && _denseFrameCount > 0)
        {
            var position = Math.Clamp((time - _denseBeginTime) * _denseSampleRate, 0f, _denseFrameCount - 1);
            var frame = Math.Min((int)position, _denseFrameCount - 1);
            var next = Math.Min(frame + 1, _denseFrameCount - 1);
            var blend = position - frame;
            for (var curve = 0; curve < _denseCurveCount; curve++)
            {
                var a = _dense[(frame * _denseCurveCount) + curve];
                var b = _dense[(next * _denseCurveCount) + curve];
                values[offset + curve] = a + ((b - a) * blend);
            }
        }

        offset += _denseCurveCount;
        _constant.CopyTo(values[offset..]);
    }

    private static float SampleStreamed(StreamedKey[] keys, float time)
    {
        if (keys.Length == 0)
        {
            return 0f;
        }

        // The last key at or before the time; the first frame sits at -FLT_MAX and holds the
        // curves' starting values.
        var low = 0;
        var high = keys.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (keys[middle].Time <= time)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        var key = keys[low];
        if (key.Time > time || !float.IsFinite(key.Time) || key.Time <= -float.MaxValue)
        {
            return key.D;
        }

        var x = time - key.Time;
        return (((key.A * x) + key.B) * x + key.C) * x + key.D;
    }

    // Frames of: float time, int key count, then per key an int curve index and four cubic
    // coefficients, valid from the frame's time until the curve's next key.
    private static StreamedKey[][] ReadStreamed(AssetTypeValueField data, int curveCount)
    {
        var words = UnityFields.Items(data).Select(word => word.AsUInt).ToArray();
        var keys = new List<StreamedKey>[curveCount];
        for (var curve = 0; curve < curveCount; curve++)
        {
            keys[curve] = [];
        }

        var position = 0;
        while (position + 2 <= words.Length)
        {
            var time = BitConverter.UInt32BitsToSingle(words[position]);
            var count = (int)words[position + 1];
            position += 2;
            for (var key = 0; key < count && position + 5 <= words.Length; key++, position += 5)
            {
                var curve = (int)words[position];
                if (curve < 0 || curve >= curveCount || time >= float.MaxValue)
                {
                    continue;
                }

                keys[curve].Add(new StreamedKey(
                    time,
                    BitConverter.UInt32BitsToSingle(words[position + 1]),
                    BitConverter.UInt32BitsToSingle(words[position + 2]),
                    BitConverter.UInt32BitsToSingle(words[position + 3]),
                    BitConverter.UInt32BitsToSingle(words[position + 4])));
            }
        }

        return [.. keys.Select(list => list.ToArray())];
    }

    private static float[] ReadFloats(AssetTypeValueField vector)
    {
        return [.. UnityFields.Items(vector).Select(value => value.AsFloat)];
    }

    private readonly record struct StreamedKey(float Time, float A, float B, float C, float D);
}
