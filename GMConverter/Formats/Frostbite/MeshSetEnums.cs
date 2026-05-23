namespace GMConverter.Formats.Frostbite;

// Numeric values mirror FrostyToolsuite 1.x (Plugins/MeshSetPlugin + FrostySdk/Utils.cs).
// They are what the engine writes to disk; do not renumber.

internal enum MeshType : uint
{
    Rigid = 0,
    Skinned = 1,
    Composite = 2
}

internal enum IndexBufferFormat : uint
{
    Index16 = 0,
    Index32 = 1
}

internal enum PrimitiveType : byte
{
    Point = 0,
    LineList = 1,
    LineStrip = 2,
    TriangleList = 3,
    TriangleStrip = 5,
    QuadList = 7,
    XenonRect = 8,
    TrianglePatch = 9
}

internal enum VertexElementUsage : byte
{
    Unknown = 0x00,
    Pos = 0x01,
    BoneIndices = 0x02,
    BoneIndices2 = 0x03,
    BoneWeights = 0x04,
    BoneWeights2 = 0x05,
    Normal = 0x06,
    Tangent = 0x07,
    Binormal = 0x08,
    BinormalSign = 0x09,
    Index = 0x17,
    Color0 = 0x1E,
    Color1 = 0x1F,
    TexCoord0 = 0x21,
    TexCoord1 = 0x22,
    TexCoord2 = 0x23,
    TexCoord3 = 0x24,
    TexCoord4 = 0x25,
    TexCoord5 = 0x26,
    TexCoord6 = 0x27,
    TexCoord7 = 0x28,
    DisplacementMapTexCoord = 0x29,
    RadiosityTexCoord = 0x2A,
    PackedTexCoord0 = 0x2D,
    PackedTexCoord1 = 0x2E,
    PackedTexCoord2 = 0x2F,
    PackedTexCoord3 = 0x30,
    TangentSpace = 0x34,
    BlendWeights = 0x65,
    VertIndex = 0xFA
}

internal enum VertexElementFormat : byte
{
    None = 0x00,
    Float = 0x01,
    Float2 = 0x02,
    Float3 = 0x03,
    Float4 = 0x04,
    Half = 0x05,
    Half2 = 0x06,
    Half3 = 0x07,
    Half4 = 0x08,
    Byte4 = 0x0A,
    Byte4N = 0x0B,
    UByte4 = 0x0C,
    UByte4N = 0x0D,
    Short = 0x0E,
    Short2 = 0x0F,
    Short3 = 0x10,
    Short4 = 0x11,
    ShortN = 0x12,
    Short2N = 0x13,
    Short3N = 0x14,
    Short4N = 0x15,
    UShort2 = 0x16,
    UShort4 = 0x17,
    UShort2N = 0x18,
    UShort4N = 0x19,
    Comp3_10_10_10 = 0x26,
    Comp3N_10_10_10 = 0x27,
    Comp3_11_11_10 = 0x2A,
    Comp3N_11_11_10 = 0x2B,
    Comp4_10_10_10_2 = 0x2E,
    UByteN = 0x32,
    Int3 = 0x33,
    UInt3 = 0x34
}

internal enum VertexElementClassification : byte
{
    PerVertex = 0,
    PerInstance = 1
}
