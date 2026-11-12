using System.Buffers.Binary;
using System.Globalization;
using GMConverter.Godot.Resources;
using GMConverter.SDK.Audio;
using GMConverter.SDK.Common;
using NVorbis;

namespace GMConverter.Godot.Audio;

/// <summary>
/// Decodes the audio resources Godot's importers write: <c>.oggvorbisstr</c> files
/// (<c>AudioStreamOggVorbis</c>) and <c>.sample</c> files (<c>AudioStreamWAV</c>).
/// </summary>
internal static class GodotAudio
{
    /// <summary>File extension of an imported Ogg Vorbis stream.</summary>
    public const string OggExtension = ".oggvorbisstr";

    /// <summary>File extension of an imported WAV sample.</summary>
    public const string SampleExtension = ".sample";

    private const int _defaultMixRate = 44100;

    public static SoundClip Decode(GodotResource stream, string name)
    {
        return stream.Type switch
        {
            "AudioStreamOggVorbis" => DecodeOgg(stream, name),
            "AudioStreamWAV" => DecodeSample(stream, name),
            _ => throw new GMConverterException($"Godot {stream.Type} audio is not supported."),
        };
    }

    /// <summary>The Ogg Vorbis file an <c>AudioStreamOggVorbis</c> was imported from, rebuilt from its pages.</summary>
    public static byte[] ToOggFile(GodotResource stream)
    {
        var (pages, granules) = ReadPackets(stream);
        return GodotOggWriter.Write(pages, granules);
    }

    private static (IReadOnlyList<byte[]>[] Pages, long[] Granules) ReadPackets(GodotResource stream)
    {
        if (stream.Get<GodotResource>("packet_sequence") is not { } sequence ||
            sequence.Get<List<object?>>("packet_data") is not { } pages)
        {
            throw new GMConverterException("Godot Ogg Vorbis stream has no packets.");
        }

        IReadOnlyList<byte[]>[] packets = [.. pages.Select(page => (IReadOnlyList<byte[]>)[.. ((page as List<object?>) ?? []).OfType<byte[]>()])];
        long[] granules = sequence["granule_positions"] switch
        {
            long[] values => values,
            int[] values => [.. values.Select(value => (long)value)],
            _ => [],
        };
        return (packets, granules);
    }

    private static SoundClip DecodeOgg(GodotResource stream, string name)
    {
        var (pages, granules) = ReadPackets(stream);
        using var reader = new VorbisReader(new MemoryStream(GodotOggWriter.Write(pages, granules)), closeOnDispose: true);
        List<float> samples = [];
        var buffer = new float[reader.Channels * 4096];
        int read;
        while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read));
        }

        // The last page's granule position is the stream's length; NVorbis decodes the whole of the
        // last packet instead of cutting it there.
        if (granules.Length > 0 && granules[^1] > 0 && granules[^1] * reader.Channels < samples.Count)
        {
            samples.RemoveRange((int)(granules[^1] * reader.Channels), samples.Count - (int)(granules[^1] * reader.Channels));
        }

        var clip = new SoundClip(name, reader.SampleRate, reader.Channels, [.. samples]);
        if (stream.Get<bool>("loop"))
        {
            var start = (int)Math.Round(ToDouble(stream["loop_offset"]) * reader.SampleRate);
            clip = clip with { Loop = new SoundLoop(Math.Clamp(start, 0, clip.FrameCount)) };
        }

        return clip;
    }

    private static SoundClip DecodeSample(GodotResource stream, string name)
    {
        var data = stream.Get<byte[]>("data") ?? [];
        var channels = stream.Get<bool>("stereo") ? 2 : 1;
        var rate = stream["mix_rate"] is long mixRate ? (int)mixRate : _defaultMixRate;
        var format = stream["format"] is long value ? value : 0;

        // Formats: 0 signed 8-bit, 1 signed 16-bit little-endian; 2 (IMA ADPCM) and 3 (QOA) are compressed.
        float[] samples = format switch
        {
            0 => [.. data.Select(sample => (sbyte)sample / 128f)],
            1 => [.. Enumerable.Range(0, data.Length / 2).Select(index => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(index * 2)) / 32768f)],
            _ => throw new GMConverterException(string.Create(CultureInfo.InvariantCulture, $"Godot WAV format {format} (compressed) is not supported.")),
        };

        var clip = new SoundClip(name, rate, channels, samples);

        // Loop modes: 0 none, 1 forward, 2 ping-pong, 3 backward; the SDK only loops forward.
        if (stream["loop_mode"] is long loopMode and > 0)
        {
            var begin = stream["loop_begin"] is long loopBegin ? (int)loopBegin : 0;
            var end = stream["loop_end"] is long loopEnd && loopEnd > begin ? (int?)loopEnd : null;
            clip = clip with { Loop = new SoundLoop(Math.Clamp(begin, 0, clip.FrameCount), end is { } frame ? Math.Min(frame, clip.FrameCount) : null) };
        }

        return clip;
    }

    private static double ToDouble(object? value)
    {
        return value switch
        {
            double number => number,
            long number => number,
            _ => 0d,
        };
    }
}
