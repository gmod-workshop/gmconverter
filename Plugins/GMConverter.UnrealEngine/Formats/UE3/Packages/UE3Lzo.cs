using GMConverter.SDK.Common;

namespace GMConverter.UnrealEngine.Formats.UE3.Packages;

/// <summary>
/// Decompresses LZO1X blocks, which UE3 uses for compressed packages and bulk data. The stream is
/// a sequence of literal runs and back-references; every back-reference carries 0-3 trailing
/// literals in its low bits, and that count (the state) decides how the next short instruction
/// is read. It ends with a far reference of distance 16384.
/// </summary>
internal static class UE3Lzo
{
    /// <summary>Decompresses <paramref name="source"/>, which must fill <paramref name="destination"/> exactly.</summary>
    public static void Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var stream = new Stream(source, destination);
        var state = 0;
        if (source.Length > 0 && source[0] > 17)
        {
            var count = stream.Next() - 17;
            stream.CopyLiterals(count);
            state = Math.Min(count, 4);
        }

        while (true)
        {
            int instruction = stream.Next();
            int distance;
            int length;
            if (instruction < 16)
            {
                if (state == 0)
                {
                    stream.CopyLiterals(stream.Extended(instruction, 15) + 3);
                    state = 4;
                    continue;
                }

                // Short references; after a literal run of four or more they reach further.
                distance = (stream.Next() << 2) + (instruction >> 2) + (state == 4 ? 2049 : 1);
                length = state == 4 ? 3 : 2;
            }
            else if (instruction < 32)
            {
                length = stream.Extended(instruction & 7, 7) + 2;
                var word = stream.Next() | (stream.Next() << 8);
                distance = 16384 + ((instruction & 8) << 11) + (word >> 2);
                if (distance == 16384)
                {
                    break;
                }

                instruction = word;
            }
            else if (instruction < 64)
            {
                length = stream.Extended(instruction & 31, 31) + 2;
                var word = stream.Next() | (stream.Next() << 8);
                distance = (word >> 2) + 1;
                instruction = word;
            }
            else
            {
                length = instruction < 128 ? 3 + ((instruction >> 5) & 1) : 5 + ((instruction >> 5) & 3);
                distance = (stream.Next() << 3) + ((instruction >> 2) & 7) + 1;
            }

            stream.CopyMatch(distance, length);
            state = instruction & 3;
            stream.CopyLiterals(state);
        }

        if (stream.Output != destination.Length)
        {
            throw Corrupt($"decompressed to {stream.Output} bytes instead of {destination.Length}");
        }
    }

    private static GMConverterException Corrupt(string problem) => new($"UE3 LZO block {problem}.");

    private ref struct Stream(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        private readonly ReadOnlySpan<byte> _source = source;
        private readonly Span<byte> _destination = destination;
        private int _input;

        public int Output { get; private set; }

        public byte Next()
        {
            if (_input >= _source.Length)
            {
                throw Corrupt("ends inside an instruction");
            }

            return _source[_input++];
        }

        // A length field of zero means "long": a run of zero bytes adds 255 each, then a final byte.
        public int Extended(int bits, int @base)
        {
            if (bits != 0)
            {
                return bits;
            }

            var length = @base;
            byte value;
            while ((value = Next()) == 0)
            {
                length += 255;
            }

            return length + value;
        }

        public void CopyLiterals(int count)
        {
            if (count > _source.Length - _input || count > _destination.Length - Output)
            {
                throw Corrupt("has a literal run past the end of its data");
            }

            _source.Slice(_input, count).CopyTo(_destination[Output..]);
            _input += count;
            Output += count;
        }

        public void CopyMatch(int distance, int length)
        {
            if (distance > Output || length > _destination.Length - Output)
            {
                throw Corrupt("refers outside its data");
            }

            // Byte by byte: a reference may overlap the bytes it produces.
            for (var i = 0; i < length; i++)
            {
                _destination[Output] = _destination[Output - distance];
                Output++;
            }
        }
    }
}
