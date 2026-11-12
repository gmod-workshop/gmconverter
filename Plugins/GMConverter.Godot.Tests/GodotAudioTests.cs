using System.Buffers.Binary;
using GMConverter.Godot.Audio;
using GMConverter.Godot.Resources;
using OggVorbisEncoder;
using static GMConverter.Godot.Tests.GodotResourceBuilder;

namespace GMConverter.Godot.Tests;

public sealed class GodotAudioTests
{
    private const int _sampleRate = 44100;

    [Fact]
    public void DecodesOggPacketSequenceToItsLength()
    {
        // A second of a 441 Hz tone, kept the way Godot imports Ogg files: packets by page. The
        // last page's granule position is the stream's length.
        var (pages, granules) = EncodeTone(_sampleRate, 441f, packetsPerPage: 10);
        var builder = new GodotResourceBuilder();
        var sequence = builder.Add("OggPacketSequence_1", "OggPacketSequence",
            ("packet_data", Array([.. pages.Select(page => Array([.. page.Select(Bytes)]))])),
            ("granule_positions", Int64s([.. granules])),
            ("sampling_rate", Float(_sampleRate)));
        builder.Add("AudioStreamOggVorbis_1", "AudioStreamOggVorbis", ("packet_sequence", Internal(sequence)), ("loop", Bool(true)));

        var clip = GodotAudio.Decode(GodotResourceFile.Read(builder.Build("AudioStreamOggVorbis")).Main, "tone");

        Assert.Equal(_sampleRate, clip.SampleRate);
        Assert.Equal(1, clip.Channels);
        Assert.Equal(granules[^1], clip.FrameCount);
        Assert.Equal(0, clip.Loop?.StartFrame);

        // 441 Hz crosses zero 882 times a second; Vorbis is lossy, so allow a few either way.
        var crossings = clip.Samples.Skip(1000).Zip(clip.Samples.Skip(1001)).Count(pair => pair.First < 0f != pair.Second < 0f);
        var expected = 882.0 * (clip.FrameCount - 1000) / _sampleRate;
        Assert.InRange(crossings, expected - 5, expected + 5);
    }

    [Fact]
    public void SplitsPagesWithMoreThan255LacingValues()
    {
        var large = Enumerable.Range(0, 70000).Select(index => (byte)index).ToArray();
        var ogg = GodotOggWriter.Write([[[1, 2, 3]], [large, [4, 5]]], [0, 100]);

        var pages = ReadPages(ogg);

        Assert.True(pages.Count > 2);
        Assert.Equal(2, pages[0].HeaderType); // beginning of stream
        Assert.Equal(4, pages[^1].HeaderType & 4); // end of stream
        Assert.All(pages.Skip(2), page => Assert.Equal(1, page.HeaderType & 1)); // continuing the large packet
        Assert.Equal(100, pages[^1].Granule);
        Assert.Equal(-1, pages[1].Granule);
        Assert.Equal<byte>([.. large, 4, 5], [.. pages.Skip(1).SelectMany(page => page.Body)]);
    }

    [Fact]
    public void DecodesSixteenBitSample()
    {
        var data = new byte[8];
        BinaryPrimitives.WriteInt16LittleEndian(data, short.MaxValue);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), short.MinValue);
        var builder = new GodotResourceBuilder();
        builder.Add("AudioStreamWAV_1", "AudioStreamWAV",
            ("data", Bytes(data)),
            ("format", Int(1)),
            ("mix_rate", Int(22050)),
            ("stereo", Bool(true)),
            ("loop_mode", Int(1)),
            ("loop_end", Int(2)));

        var clip = GodotAudio.Decode(GodotResourceFile.Read(builder.Build("AudioStreamWAV")).Main, "beep");

        Assert.Equal(22050, clip.SampleRate);
        Assert.Equal(2, clip.Channels);
        Assert.Equal(2, clip.FrameCount);
        Assert.Equal(32767f / 32768f, clip.Samples[0]);
        Assert.Equal(-1f, clip.Samples[1]);
        Assert.Equal(new SDK.Audio.SoundLoop(0, 2), clip.Loop);
    }

    private static (List<List<byte[]>> Pages, List<long> Granules) EncodeTone(int frames, float frequency, int packetsPerPage)
    {
        var info = VorbisInfo.InitVariableBitRate(1, _sampleRate, 0.5f);
        List<List<byte[]>> pages =
        [
            [HeaderPacketBuilder.BuildInfoPacket(info).PacketData],
            [HeaderPacketBuilder.BuildCommentsPacket(new Comments()).PacketData, HeaderPacketBuilder.BuildBooksPacket(info).PacketData],
        ];
        List<long> granules = [0, 0];

        var state = ProcessingState.Create(info);
        var samples = new float[1][];
        samples[0] = [.. Enumerable.Range(0, frames).Select(index => 0.5f * MathF.Sin(2f * MathF.PI * frequency * index / _sampleRate))];
        state.WriteData(samples, frames);
        state.WriteEndOfStream();

        List<byte[]> page = [];
        var granule = 0L;
        while (state.PacketOut(out var packet))
        {
            page.Add(packet.PacketData);
            granule = packet.GranulePosition;
            if (page.Count == packetsPerPage || packet.EndOfStream)
            {
                pages.Add(page);
                granules.Add(granule);
                page = [];
            }
        }

        if (page.Count > 0)
        {
            pages.Add(page);
            granules.Add(granule);
        }

        return (pages, granules);
    }

    private static List<(int HeaderType, long Granule, byte[] Body)> ReadPages(byte[] ogg)
    {
        List<(int, long, byte[])> pages = [];
        var offset = 0;
        while (offset < ogg.Length)
        {
            Assert.True(ogg.AsSpan(offset, 4).SequenceEqual("OggS"u8));
            var segments = ogg[offset + 26];
            var bodyLength = ogg.AsSpan(offset + 27, segments).ToArray().Sum(value => value);
            var bodyStart = offset + 27 + segments;
            pages.Add((ogg[offset + 5], BinaryPrimitives.ReadInt64LittleEndian(ogg.AsSpan(offset + 6)), ogg[bodyStart..(bodyStart + bodyLength)]));
            offset = bodyStart + bodyLength;
        }

        return pages;
    }
}
