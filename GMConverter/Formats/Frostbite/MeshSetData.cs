using System.Numerics;

namespace GMConverter.Formats.Frostbite;

// Parsed, in-memory shape of a Frostbite MeshSet RES blob plus its referenced chunk descriptors.
// The records here are deliberately "what the file said" — they do not interpret semantics
// (e.g., they don't transform PrimitiveType into a triangle list, don't unpack vertex streams).
// Downstream consumers handle that.

internal sealed record FrostbiteMeshSet(
    string Name,
    string ShortName,
    Vector3 BoundingBoxMin,
    Vector3 BoundingBoxMax,
    MeshType MeshType,
    uint Flags,
    IReadOnlyList<FrostbiteMeshLod> Lods);

internal sealed record FrostbiteMeshLod(
    int LodIndex,
    MeshType MeshType,
    uint Flags,
    IndexBufferFormat IndexBufferFormat,
    uint IndexBufferSize,
    uint VertexBufferSize,
    int AdjacencyBufferSize,
    Guid ChunkId,
    uint InlineDataOffset,
    string Name,
    string ShortName,
    IReadOnlyList<FrostbiteMeshSection> Sections,
    // Section indices (0-based within this LOD) classified as Opaque / Transparent /
    // TransparentDecal by the MeshSet's subset-category table. Sections NOT in this set are
    // depth-only (ZOnly) or shadow proxies and must not be rendered as visible geometry — they
    // overlap the renderable sections with stripped vertex layouts.
    IReadOnlyCollection<int> RenderableSectionIndices);

internal sealed record FrostbiteMeshSection(
    string MaterialName,
    int MaterialId,
    uint PrimitiveCount,
    uint StartIndex,
    uint VertexOffset,
    uint VertexCount,
    byte VertexStride,
    PrimitiveType PrimitiveType,
    byte BonesPerVertex,
    int BoneCount,
    IReadOnlyList<FrostbiteVertexDeclaration> Declarations,
    IReadOnlyList<float> TexCoordRatios);

// A GeometryDeclarationDesc instance — describes one possible vertex layout the section can be
// fed through. Sections in SWBF2/SWS hold two of these (the second is typically a depth-only or
// shadow declaration). The first is the renderable one.
internal sealed record FrostbiteVertexDeclaration(
    IReadOnlyList<FrostbiteVertexElement> Elements,
    IReadOnlyList<FrostbiteVertexStream> Streams);

internal readonly record struct FrostbiteVertexElement(
    VertexElementUsage Usage,
    VertexElementFormat Format,
    byte Offset,
    byte StreamIndex);

internal readonly record struct FrostbiteVertexStream(
    byte VertexStride,
    VertexElementClassification Classification);
