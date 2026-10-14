namespace GMConverter.Unity.Geometry;

/// <summary>The component format of a vertex channel (Unity 2019 and later).</summary>
internal enum UnityVertexFormat : byte
{
    Float = 0,
    Float16 = 1,
    UNorm8 = 2,
    SNorm8 = 3,
    UNorm16 = 4,
    SNorm16 = 5,
    UInt8 = 6,
    SInt8 = 7,
    UInt16 = 8,
    SInt16 = 9,
    UInt32 = 10,
    SInt32 = 11,
}
