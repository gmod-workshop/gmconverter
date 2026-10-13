using System.Buffers.Binary;
using System.Text;
using GMConverter.SDK.Audio;
using GMConverter.SDK.Common;

namespace GMConverter.SourceEngine.Audio;

/// <summary>
/// Reads and writes RIFF WAVE files. Reading takes the PCM and float encodings audio tools write;
/// writing produces what Source plays: 16-bit PCM, with a <c>cue </c> chunk marking where a looping
/// sound starts its loop, because Source loops a WAV from its first cue point to its end.
/// </summary>
internal static class WaveFile
{
    private const ushort _pcmFormat = 1;
    private const ushort _floatFormat = 3;
    private const ushort _extensibleFormat = 0xFFFE;

    public static SoundClip Read(string path)
    {
        return Read(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path), path);
    }

    public static SoundClip Read(ReadOnlySpan<byte> data, string name, string source)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data[8..12].SequenceEqual("WAVE"u8))
        {
            throw new GMConverterException($"Not a RIFF WAVE file: {source}");
        }

        ushort format = 0, channels = 0, bitsPerSample = 0;
        var sampleRate = 0;
        ReadOnlySpan<byte> samples = default;
        var hasSamples = false;
        SoundLoop? sampleLoop = null;
        int? firstCue = null;
        var offset = 12;
        while (offset + 8 <= data.Length)
        {
            var id = data.Slice(offset, 4);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4)), (uint)(data.Length - offset - 8));
            var body = data.Slice(offset + 8, size);
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                if (format == _extensibleFormat && size >= 26)
                {
                    // The sub-format GUID starts with the plain format tag.
                    format = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);
                }
            }
            else if (id.SequenceEqual("data"u8))
            {
                samples = body;
                hasSamples = true;
            }
            else if (id.SequenceEqual("smpl"u8) && size >= 36 + 24 && BinaryPrimitives.ReadInt32LittleEndian(body[28..]) > 0)
            {
                // The first sample loop; its end is the last frame played, inclusive.
                var start = BinaryPrimitives.ReadInt32LittleEndian(body[44..]);
                var end = BinaryPrimitives.ReadInt32LittleEndian(body[48..]);
                sampleLoop = new SoundLoop(start, end + 1);
            }
            else if (id.SequenceEqual("cue "u8) && size >= 4 + 24 && BinaryPrimitives.ReadInt32LittleEndian(body) > 0)
            {
                firstCue = BinaryPrimitives.ReadInt32LittleEndian(body[24..]);
            }

            // Chunks are padded to an even size.
            offset += 8 + size + (size & 1);
        }

        if (channels == 0 || sampleRate <= 0 || !hasSamples)
        {
            throw new GMConverterException($"WAVE file has no format or sample data: {source}");
        }

        // A cue point on its own is how Source marks a loop, so a Source WAV with one loops from there.
        return new SoundClip(name, sampleRate, channels, DecodeSamples(samples, format, bitsPerSample, source))
        {
            Loop = sampleLoop ?? (firstCue is { } cue ? new SoundLoop(cue) : null),
        };
    }

    private static float[] DecodeSamples(ReadOnlySpan<byte> data, ushort format, ushort bitsPerSample, string source)
    {
        var bytesPerSample = bitsPerSample / 8;
        if (bytesPerSample == 0)
        {
            throw new GMConverterException($"Unsupported WAVE sample size {bitsPerSample} bits: {source}");
        }

        var samples = new float[data.Length / bytesPerSample];
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = data.Slice(i * bytesPerSample, bytesPerSample);
            samples[i] = (format, bitsPerSample) switch
            {
                (_pcmFormat, 8) => (sample[0] - 128) / 128f,
                (_pcmFormat, 16) => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
                (_pcmFormat, 24) => ((sample[0] | (sample[1] << 8) | (sample[2] << 16)) << 8 >> 8) / 8388608f,
                (_pcmFormat, 32) => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f,
                (_floatFormat, 32) => BinaryPrimitives.ReadSingleLittleEndian(sample),
                (_floatFormat, 64) => (float)BinaryPrimitives.ReadDoubleLittleEndian(sample),
                _ => throw new GMConverterException($"Unsupported WAVE encoding (format {format}, {bitsPerSample} bits): {source}"),
            };
        }

        return samples;
    }

    /// <summary>
    /// Writes <paramref name="clip"/> as 16-bit PCM. A looping clip gets a cue point at its loop
    /// start and is cut at its loop end, since Source loops to the end of the file.
    /// </summary>
    public static void Write(string path, SoundClip clip)
    {
        var frames = clip.FrameCount;
        if (clip.Loop?.EndFrame is { } loopEnd)
        {
            frames = Math.Clamp(loopEnd, 0, frames);
        }

        var dataSize = frames * clip.Channels * sizeof(short);
        var cueSize = clip.Loop is null ? 0 : 8 + 4 + 24;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write("RIFF"u8);
        writer.Write(4 + (8 + 16) + (8 + dataSize) + cueSize);
        writer.Write("WAVE"u8);

        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write(_pcmFormat);
        writer.Write((ushort)clip.Channels);
        writer.Write(clip.SampleRate);
        writer.Write(clip.SampleRate * clip.Channels * sizeof(short));
        writer.Write((ushort)(clip.Channels * sizeof(short)));
        writer.Write((ushort)16);

        writer.Write("data"u8);
        writer.Write(dataSize);
        var count = frames * clip.Channels;
        for (var i = 0; i < count; i++)
        {
            writer.Write((short)Math.Round(Math.Clamp(clip.Samples[i], -1f, 1f) * 32767f));
        }

        if (clip.Loop is { } loop)
        {
            var start = Math.Clamp(loop.StartFrame, 0, Math.Max(frames - 1, 0));
            writer.Write("cue "u8);
            writer.Write(4 + 24);
            writer.Write(1);
            // One cue point: id, position, the chunk it points into, two offsets unused for plain
            // PCM data, and the frame.
            writer.Write(1);
            writer.Write(start);
            writer.Write("data"u8);
            writer.Write(0);
            writer.Write(0);
            writer.Write(start);
        }
    }
}
