namespace GMConverter.MenOfWar.Formats;

[Flags]
internal enum MOWAnimationChunkType : ushort
{
    Position = 1,
    Quaternion = 2,
    InvertedQuaternion = 4,
    Unknown = 8,
    Unknown2 = 16,
    Vertices = 32
}
