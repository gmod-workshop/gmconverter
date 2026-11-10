using System.Text;
using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UE3LzoTests
{
    // Every stream ends with 0x11 0x00 0x00: a far reference of distance 16384, LZO1X's end marker.
    [Theory]
    // First byte 17+3: three literals. 0x27 = 001 00111: copy 7+2 bytes from 3 back (word 0x0008 >> 2, plus one).
    [InlineData(new byte[] { 0x14, (byte)'a', (byte)'b', (byte)'c', 0x27, 0x08, 0x00, 0x11, 0x00, 0x00 }, "abcabcabcabc")]
    // First byte under 18 is an instruction; with nothing before it, 0x01 is a run of 1+3 literals.
    [InlineData(new byte[] { 0x01, (byte)'w', (byte)'x', (byte)'y', (byte)'z', 0x11, 0x00, 0x00 }, "wxyz")]
    // After three literals, 0x08 = 0000 1000 is a two-byte reference 2+1 back.
    [InlineData(new byte[] { 0x14, (byte)'a', (byte)'b', (byte)'c', 0x08, 0x00, 0x11, 0x00, 0x00 }, "abcab")]
    // 0x6C = 011 0 11 00: 3+1 bytes from 3+1 back.
    [InlineData(new byte[] { 0x15, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 0x6C, 0x00, 0x11, 0x00, 0x00 }, "abcdabcd")]
    // 0xE4 = 111 001 00: 5+3 bytes from 1+1 back, overlapping what it writes.
    [InlineData(new byte[] { 0x13, (byte)'x', (byte)'y', 0xE4, 0x00, 0x11, 0x00, 0x00 }, "xyxyxyxyxy")]
    // 0x6D: as 0x6C, then one trailing literal from the reference's low bits.
    [InlineData(new byte[] { 0x15, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 0x6D, 0x00, (byte)'!', 0x11, 0x00, 0x00 }, "abcdabcd!")]
    public void DecodesEachInstruction(byte[] stream, string expected)
    {
        var output = new byte[expected.Length];

        UE3Lzo.Decompress(stream, output);

        Assert.Equal(expected, Encoding.ASCII.GetString(output));
    }

    [Fact]
    public void ReadsLongLiteralRunsAndFarShortReferences()
    {
        // A literal run of 2052: length field 0, seven zero bytes (7*255), then 249: 15+1785+249+3.
        // After a run of four or more, 0x00 0x00 is a three-byte reference 2049 back.
        var literals = Enumerable.Range(0, 2052).Select(i => (byte)(i * 7 + 1)).ToArray();
        byte[] stream = [0x00, 0, 0, 0, 0, 0, 0, 0, 249, .. literals, 0x00, 0x00, 0x11, 0x00, 0x00];
        var output = new byte[2055];

        UE3Lzo.Decompress(stream, output);

        Assert.Equal(literals, output[..2052]);
        Assert.Equal(literals[3..6], output[2052..]);
    }

    [Theory]
    [InlineData(new byte[] { 0x14, (byte)'a', (byte)'b' })]
    [InlineData(new byte[] { 0x14, (byte)'a', (byte)'b', (byte)'c', 0x27, 0x20, 0x00, 0x11, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x14, (byte)'a', (byte)'b', (byte)'c', 0x11, 0x00, 0x00 })]
    public void RejectsTruncatedOrInconsistentStreams(byte[] stream)
    {
        Assert.Throws<GMConverterException>(() => UE3Lzo.Decompress(stream, new byte[12]));
    }
}
