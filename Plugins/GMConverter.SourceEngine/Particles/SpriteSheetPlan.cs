using GMConverter.SDK.Particles;

namespace GMConverter.SourceEngine.Particles;

/// <summary>
/// How a flipbook becomes sprite sheet sequences. A frame curve becomes one sequence whose frame
/// durations follow the curve, stretched over each particle's life; no curve gives one single-frame
/// sequence per frame, picked at random per particle.
/// </summary>
internal sealed record SpriteSheetPlan(IReadOnlyList<SpriteSequence> Sequences, bool RandomSequence)
{
    private const int _samples = 64;

    public static SpriteSheetPlan Create(ParticleSheet sheet)
    {
        var count = Math.Max(1, sheet.FrameCount);
        if (sheet.FrameOverLife is not { } curve)
        {
            return new SpriteSheetPlan([.. Enumerable.Range(0, count).Select(frame => new SpriteSequence([(frame, 1f)]))], RandomSequence: true);
        }

        List<(int Frame, float Duration)> frames = [];
        for (var step = 0; step < _samples; step++)
        {
            var time = (step + 0.5f) / _samples;
            var frame = Math.Clamp((int)MathF.Floor(curve.Evaluate(time, 0.5f)), 0, count - 1);
            if (frames.Count > 0 && frames[^1].Frame == frame)
            {
                frames[^1] = (frame, frames[^1].Duration + (1f / _samples));
            }
            else
            {
                frames.Add((frame, 1f / _samples));
            }
        }

        return new SpriteSheetPlan([new SpriteSequence(frames)], RandomSequence: false);
    }
}
