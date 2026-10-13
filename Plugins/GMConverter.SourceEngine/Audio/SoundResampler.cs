using GMConverter.SDK.Audio;

namespace GMConverter.SourceEngine.Audio;

/// <summary>Changes a clip's sample rate or channel count to one Source can play.</summary>
internal static class SoundResampler
{
    // Taps on each side of the windowed-sinc filter at the input rate. 16 keeps the passband flat
    // to well past 18 kHz at 44.1 kHz without making long clips slow to convert.
    private const int _halfTaps = 16;

    /// <summary>Averages every channel into one.</summary>
    public static SoundClip ToMono(SoundClip clip)
    {
        if (clip.Channels == 1)
        {
            return clip;
        }

        var frames = clip.FrameCount;
        var mono = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < clip.Channels; channel++)
            {
                sum += clip.Samples[frame * clip.Channels + channel];
            }

            mono[frame] = sum / clip.Channels;
        }

        return clip with { Channels = 1, Samples = mono };
    }

    /// <summary>
    /// Resamples with a Blackman-windowed sinc filter. When lowering the rate, the filter's cutoff
    /// drops to the new Nyquist frequency so nothing above it folds back as aliasing.
    /// </summary>
    public static SoundClip Resample(SoundClip clip, int sampleRate)
    {
        if (clip.SampleRate == sampleRate || clip.FrameCount == 0)
        {
            return clip;
        }

        var ratio = (double)sampleRate / clip.SampleRate;
        var cutoff = Math.Min(1.0, ratio);
        var width = _halfTaps / cutoff;
        var inFrames = clip.FrameCount;
        var outFrames = (int)Math.Round(inFrames * ratio);
        var channels = clip.Channels;
        var output = new float[outFrames * channels];
        for (var frame = 0; frame < outFrames; frame++)
        {
            var center = frame / ratio;
            var first = Math.Max(0, (int)Math.Ceiling(center - width));
            var last = Math.Min(inFrames - 1, (int)Math.Floor(center + width));
            for (var channel = 0; channel < channels; channel++)
            {
                double sum = 0, weights = 0;
                for (var input = first; input <= last; input++)
                {
                    var weight = Kernel(center - input, cutoff, width);
                    sum += clip.Samples[input * channels + channel] * weight;
                    weights += weight;
                }

                output[frame * channels + channel] = weights == 0 ? 0f : (float)(sum / weights);
            }
        }

        return clip with
        {
            SampleRate = sampleRate,
            Samples = output,
            Loop = clip.Loop is { } loop
                ? new SoundLoop((int)Math.Round(loop.StartFrame * ratio), loop.EndFrame is { } end ? (int)Math.Round(end * ratio) : null)
                : null,
        };
    }

    private static double Kernel(double distance, double cutoff, double width)
    {
        if (Math.Abs(distance) >= width)
        {
            return 0;
        }

        var x = Math.PI * cutoff * distance;
        var sinc = x == 0 ? 1 : Math.Sin(x) / x;
        var phase = Math.PI * (distance / width + 1);
        var window = 0.42 - 0.5 * Math.Cos(phase) + 0.08 * Math.Cos(2 * phase);
        return sinc * window;
    }
}
