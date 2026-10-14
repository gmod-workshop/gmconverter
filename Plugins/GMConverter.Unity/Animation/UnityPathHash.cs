using System.Text;

namespace GMConverter.Unity.Animation;

/// <summary>Unity's hash of a transform path (<c>Hips/Spine</c>) below an animated root: CRC-32 of its UTF-8 bytes.</summary>
internal static class UnityPathHash
{
    private static readonly uint[] _table = BuildTable();

    public static uint Compute(string path)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in Encoding.UTF8.GetBytes(path))
        {
            crc = _table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (var index = 0u; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
