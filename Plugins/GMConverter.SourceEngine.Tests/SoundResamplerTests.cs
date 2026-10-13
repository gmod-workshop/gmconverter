using GMConverter.SDK.Audio;
using GMConverter.SourceEngine.Audio;
using GMConverter.SourceEngine.Exporters;

namespace GMConverter.SourceEngine.Tests;

public sealed class SoundResamplerTests
{
    [Fact]
    public void ResamplingKeepsDurationPitchAndLevel()
    {
        // One second of a 1 kHz tone at Wwise's usual 48 kHz.
        var tone = Enumerable.Range(0, 48000).Select(i => 0.5f * MathF.Sin(2 * MathF.PI * 1000 * i / 48000f)).ToArray();

        var resampled = SoundResampler.Resample(new SoundClip("tone", 48000, 1, tone), 44100);

        Assert.Equal(44100, resampled.SampleRate);
        Assert.Equal(44100, resampled.FrameCount);
        // Away from the edges, every sample matches the same tone sampled at the new rate.
        for (var i = 1000; i < 43000; i += 97)
        {
            Assert.Equal(0.5f * MathF.Sin(2 * MathF.PI * 1000 * i / 44100f), resampled.Samples[i], 0.01f);
        }
    }

    [Fact]
    public void DownsamplingFiltersOutWhatTheNewRateCannotHold()
    {
        // 15 kHz is above 11025 Hz's Nyquist frequency, so it must not fold back as a lower tone.
        var tone = Enumerable.Range(0, 44100).Select(i => MathF.Sin(2 * MathF.PI * 15000 * i / 44100f)).ToArray();

        var resampled = SoundResampler.Resample(new SoundClip("tone", 44100, 1, tone), 11025);

        var middle = resampled.Samples.Skip(1000).Take(9000).ToArray();
        Assert.True(middle.Max(MathF.Abs) < 0.05f);
    }

    [Fact]
    public void LoopPointsScaleWithTheRate()
    {
        var clip = new SoundClip("loop", 22050, 1, new float[22050]) { Loop = new SoundLoop(11025, 22050) };

        Assert.Equal(new SoundLoop(22050, 44100), SoundResampler.Resample(clip, 44100).Loop);
    }

    [Fact]
    public void MonoAveragesTheChannels()
    {
        var mono = SoundResampler.ToMono(new SoundClip("stereo", 44100, 2, [1f, 0f, 0.5f, -0.5f]));

        Assert.Equal(1, mono.Channels);
        Assert.Equal([0.5f, 0f], mono.Samples);
    }

    [Theory]
    [InlineData(44100, 44100)]
    [InlineData(22050, 22050)]
    [InlineData(48000, 44100)]
    [InlineData(32000, 44100)]
    [InlineData(16000, 22050)]
    [InlineData(8000, 11025)]
    public void AutoSampleRateKeepsSourceRatesAndOtherwiseGoesUp(int input, int expected)
    {
        Assert.Equal(expected, WAVExporter.TargetSampleRate(input, "auto"));
    }
}
