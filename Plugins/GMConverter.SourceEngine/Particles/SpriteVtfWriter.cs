using System.Text;

namespace GMConverter.SourceEngine.Particles;

/// <summary>
/// Writes a particle texture as a VTF 7.4: uncompressed RGBA8888 with a full mip chain, clamped, and
/// optionally a sprite sheet resource (tag 0x10) describing flipbook sequences over a grid of frames.
/// VTFCmd can't write sheet resources. Resources are listed in tag order (the sheet before the image),
/// which Garry's Mod requires.
/// </summary>
internal static class SpriteVtfWriter
{
    private const int _headerSize = 80;
    private const uint _flagClampS = 0x4;
    private const uint _flagClampT = 0x8;
    private const uint _flagEightBitAlpha = 0x2000;
    private const int _formatRgba8888 = 0;

    public static void Write(string path, byte[] rgba, int width, int height, int columns, int rows, IReadOnlyList<SpriteSequence>? sequences)
    {
        var mips = Mips(rgba, width, height);
        byte[]? sheet = sequences is { Count: > 0 } ? Sheet(sequences, columns, rows) : null;
        var resourceCount = sheet is null ? 1 : 2;
        var dataStart = Align(_headerSize + (resourceCount * 8), 16);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write("VTF\0"u8);
        writer.Write(7);
        writer.Write(4);
        writer.Write(dataStart);
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write(_flagClampS | _flagClampT | _flagEightBitAlpha);
        writer.Write((ushort)1); // frames
        writer.Write((ushort)0); // first frame
        writer.Write(0); // padding
        writer.Write(Reflectivity(rgba));
        writer.Write(0); // padding
        writer.Write(1f); // bump scale
        writer.Write(_formatRgba8888);
        writer.Write((byte)mips.Count);
        writer.Write(-1); // no low-res image
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1); // depth
        writer.Write(new byte[3]);
        writer.Write(resourceCount);
        writer.Write(new byte[8]);

        var imageOffset = dataStart + (sheet is null ? 0 : 4 + sheet.Length);
        if (sheet is not null)
        {
            writer.Write(0x10); // tag 10 00 00, flags 0
            writer.Write(dataStart);
        }

        writer.Write(0x30); // tag 30 00 00, flags 0
        writer.Write(imageOffset);
        writer.Write(new byte[dataStart - (int)stream.Position]);
        if (sheet is not null)
        {
            writer.Write(sheet.Length);
            writer.Write(sheet);
        }

        // Smallest mip first.
        for (var level = mips.Count - 1; level >= 0; level--)
        {
            writer.Write(mips[level]);
        }
    }

    // mksheet's version 0 layout: each frame is a duration and one UV rectangle.
    private static byte[] Sheet(IReadOnlyList<SpriteSequence> sequences, int columns, int rows)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0); // version
        writer.Write(sequences.Count);
        for (var index = 0; index < sequences.Count; index++)
        {
            var frames = sequences[index].Frames;
            writer.Write(index);
            writer.Write(0); // loops rather than clamping
            writer.Write(frames.Count);
            writer.Write(frames.Sum(frame => frame.Duration));
            foreach (var (frame, duration) in frames)
            {
                var (column, row) = (frame % columns, frame / columns);
                writer.Write(duration);
                writer.Write((float)column / columns);
                writer.Write((float)row / rows);
                writer.Write((float)(column + 1) / columns);
                writer.Write((float)(row + 1) / rows);
            }
        }

        return stream.ToArray();
    }

    private static List<byte[]> Mips(byte[] rgba, int width, int height)
    {
        List<byte[]> mips = [rgba];
        var (w, h) = (width, height);
        while (w > 1 || h > 1)
        {
            var (nw, nh) = (Math.Max(1, w / 2), Math.Max(1, h / 2));
            var source = mips[^1];
            var next = new byte[nw * nh * 4];
            for (var y = 0; y < nh; y++)
            {
                for (var x = 0; x < nw; x++)
                {
                    for (var channel = 0; channel < 4; channel++)
                    {
                        var sum = 0;
                        for (var dy = 0; dy < 2; dy++)
                        {
                            for (var dx = 0; dx < 2; dx++)
                            {
                                var sx = Math.Min(w - 1, (x * 2) + dx);
                                var sy = Math.Min(h - 1, (y * 2) + dy);
                                sum += source[(((sy * w) + sx) * 4) + channel];
                            }
                        }

                        next[(((y * nw) + x) * 4) + channel] = (byte)((sum + 2) / 4);
                    }
                }
            }

            mips.Add(next);
            (w, h) = (nw, nh);
        }

        return mips;
    }

    private static void Write(this BinaryWriter writer, (float R, float G, float B) value)
    {
        writer.Write(value.R);
        writer.Write(value.G);
        writer.Write(value.B);
    }

    private static (float R, float G, float B) Reflectivity(byte[] rgba)
    {
        double r = 0, g = 0, b = 0;
        var count = Math.Max(1, rgba.Length / 4);
        for (var index = 0; index < rgba.Length; index += 4)
        {
            r += rgba[index];
            g += rgba[index + 1];
            b += rgba[index + 2];
        }

        return ((float)(r / count / 255), (float)(g / count / 255), (float)(b / count / 255));
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
