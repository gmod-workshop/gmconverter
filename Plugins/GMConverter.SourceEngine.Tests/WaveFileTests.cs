using GMConverter.SDK.Audio;

namespace GMConverter.SourceEngine.Tests;

public sealed class WaveFileTests
{
    [Fact]
    public void SixteenBitStereoRoundTrips()
    {
        var clip = new SoundClip("tone", 22050, 2, [0f, 0.5f, -0.5f, 1f, -1f, 0.25f]);

        var read = WaveFile.Read(Write(clip));

        Assert.Equal(22050, read.SampleRate);
        Assert.Equal(2, read.Channels);
        Assert.Equal(clip.Samples.Length, read.Samples.Length);
        for (var i = 0; i < clip.Samples.Length; i++)
        {
            Assert.Equal(clip.Samples[i], read.Samples[i], 0.0001f);
        }

        Assert.Null(read.Loop);
    }

    [Fact]
    public void LoopStartIsWrittenAsACuePointAndTheClipIsCutAtTheLoopEnd()
    {
        var clip = new SoundClip("loop", 44100, 1, new float[100]) { Loop = new SoundLoop(10, 80) };

        var read = WaveFile.Read(Write(clip));

        Assert.Equal(80, read.FrameCount);
        Assert.Equal(new SoundLoop(10), read.Loop);
    }

    [Fact]
    public void ReadsTwentyFourBitAndFloatSamples()
    {
        byte[] pcm24 = [0x00, 0x00, 0x40, 0x00, 0x00, 0xC0];
        var float32 = BitConverter.GetBytes(0.75f);

        Assert.Equal([0.5f, -0.5f], WaveFile.Read(Riff(1, 1, 24, pcm24), "a", "a").Samples);
        Assert.Equal([0.75f], WaveFile.Read(Riff(3, 1, 32, float32), "b", "b").Samples);
    }

    private static byte[] Riff(ushort format, ushort channels, ushort bits, byte[] data)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(4 + 24 + 8 + data.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write(format);
        writer.Write(channels);
        writer.Write(48000);
        writer.Write(48000 * channels * bits / 8);
        writer.Write((ushort)(channels * bits / 8));
        writer.Write(bits);
        writer.Write("data"u8);
        writer.Write(data.Length);
        writer.Write(data);
        writer.Flush();
        return stream.ToArray();
    }

    private static string Write(SoundClip clip)
    {
        var directory = Path.Join(Path.GetTempPath(), "GMConverter.SourceEngine.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Join(directory, $"{clip.Name}.wav");
        WaveFile.Write(path, clip);
        return path;
    }
}
