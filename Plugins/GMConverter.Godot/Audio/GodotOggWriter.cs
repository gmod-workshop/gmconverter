using System.Buffers.Binary;

namespace GMConverter.Godot.Audio;

/// <summary>
/// Writes an Ogg Vorbis file from the pages Godot keeps for an <c>AudioStreamOggVorbis</c>: Godot
/// stores each page's packets and its granule position but drops the page headers, so they are
/// rebuilt here. A page with more than 255 lacing values is split, as Ogg allows.
/// </summary>
internal static class GodotOggWriter
{
    private const int _maxSegments = 255;
    private const uint _serialNumber = 0x474F4454; // "GODT"
    private static readonly uint[] _crcTable = CreateCrcTable();

    public static byte[] Write(IReadOnlyList<IReadOnlyList<byte[]>> pages, IReadOnlyList<long> granulePositions)
    {
        using var output = new MemoryStream();
        var sequence = 0u;
        var pageCount = pages.Count(page => page.Count > 0);
        var written = 0;

        for (var page = 0; page < pages.Count; page++)
        {
            if (pages[page].Count == 0)
            {
                continue;
            }

            written++;
            var granule = page < granulePositions.Count ? granulePositions[page] : -1;
            List<byte> lacing = [];
            List<byte[]> bodies = [];

            // Godot's pages hold whole packets, so only a page split here continues one.
            var continued = false;
            var lastPage = written == pageCount;

            foreach (var packet in pages[page])
            {
                var offset = 0;
                while (true)
                {
                    if (lacing.Count == _maxSegments)
                    {
                        // The page is full: finish it without a granule position and go on with
                        // the rest on the next, marked as continuing a packet if one was cut.
                        WritePage(output, lacing, bodies, continued, isFirst: sequence == 0, isLast: false, granule: -1, sequence++);
                        lacing.Clear();
                        bodies.Clear();
                        continued = offset > 0;
                    }

                    var length = Math.Min(255, packet.Length - offset);
                    lacing.Add((byte)length);
                    bodies.Add(packet[offset..(offset + length)]);
                    offset += length;
                    if (length < 255)
                    {
                        break;
                    }
                }
            }

            WritePage(output, lacing, bodies, continued, isFirst: sequence == 0, isLast: lastPage, granule, sequence++);
        }

        return output.ToArray();
    }

    private static void WritePage(MemoryStream output, List<byte> lacing, List<byte[]> bodies, bool continued, bool isFirst, bool isLast, long granule, uint sequence)
    {
        var bodyLength = bodies.Sum(body => body.Length);
        var page = new byte[27 + lacing.Count + bodyLength];
        "OggS"u8.CopyTo(page);
        page[5] = (byte)((continued ? 1 : 0) | (isFirst ? 2 : 0) | (isLast ? 4 : 0));
        BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(6), granule);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(14), _serialNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(18), sequence);
        page[26] = (byte)lacing.Count;
        lacing.CopyTo(page, 27);
        var offset = 27 + lacing.Count;
        foreach (var body in bodies)
        {
            body.CopyTo(page, offset);
            offset += body.Length;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), Crc(page));
        output.Write(page);
    }

    // Ogg's CRC-32: polynomial 0x04C11DB7, not reflected, starting from zero.
    private static uint Crc(ReadOnlySpan<byte> data)
    {
        var crc = 0u;
        foreach (var value in data)
        {
            crc = (crc << 8) ^ _crcTable[(crc >> 24) ^ value];
        }

        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (var index = 0u; index < 256; index++)
        {
            var value = index << 24;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 0x80000000) != 0 ? (value << 1) ^ 0x04C11DB7 : value << 1;
            }

            table[index] = value;
        }

        return table;
    }
}
