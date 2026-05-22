using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using GMConverter.Common;

namespace GMConverter.Formats.Frostbite;

// Parses the binary MeshSet RES blob produced by the SWBF II (2017) / Star Wars: Squadrons
// (2020) branch of Frostbite (DataVersion 20171116 / 20201001). They share an identical layout
// in FrostyToolsuite 1.x's MeshSetPlugin — same HasNewPartBoneLayout, HasAdjacencyInMesh,
// 5-category subsets, dual GeometryDeclarationDesc per section, and 44-byte trailing unknown
// data per section (the "default" case in MeshSet.cs's switch). We do not gate on version
// internally; callers must only invoke this against blobs from one of those two profiles.
//
// All offsets below are byte positions inside the RES inline data (which is what
// AssetManager.GetAsset returns). The numbers come from cross-checking against
// Plugins/MeshSetPlugin/Resources/MeshSet.cs in github.com/CadeEvs/FrostyToolsuite (master).
internal static class MeshSetReader
{
    // Bytes consumed by a single Vec3 in this engine — three floats plus a trailing pad float.
    private const int _vec3Size = 16;

    // GeometryDeclarationDesc fields. MaxStreams is per-profile: 4 in older Frostbite, 16 for
    // SWS / SWBF II / Fifa18+ / Anthem / etc. (see GeometryDeclarationDesc.MaxStreams in
    // FrostySdk/Utils.cs). Our parser is hard-gated to SWS/SWBF II so the 16-stream layout is
    // the only one we handle here.
    private const int _maxElements = 16;
    private const int _maxStreams = 16;
    private const int _declarationStride = _maxElements * 4 + _maxStreams * 2 + 4; // 100 bytes

    // Total bytes per MeshSetSection record for SWBF II / SWS:
    //   +0..+84  fixed section header (offsets, counts, strides, bones)
    //   +84..+184  Decl0 (_declarationStride)
    //   +184..+284 Decl1
    //   +284..+308 TexCoordRatios (6 × float)
    //   +308..+352 trailing unknown data block (44 bytes — "default" case in MeshSet.cs's
    //              per-profile switch; SWS and SWBF II both fall through to that case).
    private const int _sectionStride = 84 + _declarationStride * 2 + 24 + 44;

    public static FrostbiteMeshSet Parse(ReadOnlySpan<byte> blob)
    {
        var bboxMin = ReadVec3(blob, 0);
        var bboxMax = ReadVec3(blob, _vec3Size);
        // 0..32 = bbox.

        Span<long> lodOffsets = stackalloc long[6];
        for (var i = 0; i < 6; i++)
        {
            lodOffsets[i] = BinaryPrimitives.ReadInt64LittleEndian(blob.Slice(32 + i * 8, 8));
        }

        // 80..88 unknownLong (read but unused — present for every Frostbite version except the
        // BF4/DAI/NFSR/PvZ:GW exclusion set; SWS/SWBF2 are included so we always skip it).
        var fullnameOffset = BinaryPrimitives.ReadInt64LittleEndian(blob.Slice(88, 8));
        var nameOffset = BinaryPrimitives.ReadInt64LittleEndian(blob.Slice(96, 8));
        // 104..108 NameHash.
        var meshType = (MeshType)BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(108, 4));
        // 112..136 LodFadeDistanceFactors (12 × ushort).
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(136, 4));
        // 140..144 ShaderDrawOrder block (byte+byte+short).
        var lodCount = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(144, 2));
        // 146..148 SectionCount (read but not surfaced — section counts come per-LOD).

        if (lodCount > 6)
        {
            throw new GMConverterException(
                $"MeshSet declares LodCount={lodCount} which exceeds the engine's MaxLodCount of 6. Blob is malformed or from an unsupported engine variant.");
        }

        // Skinned/Composite write a bone-part block between the header and the first LOD body,
        // but we reach LODs through their absolute offsets so the block isn't walked here. The
        // Composite LOD record adds an extra long (bonePartOffset03) between unknownLong and the
        // 16-byte pad, but both Rigid and Composite happen to land at the same 176-byte stride
        // after padding, so the LOD reader doesn't branch either. We do not yet apply per-part
        // transforms — most Squadrons Composite assets are environment props whose geometry is
        // already in usable local space; if a specific asset comes out misplaced, revisit by
        // reading the LOD's bonePartOffset03 + 24-byte/section part bitmask and applying the
        // header's PartTransforms entry.
        if (meshType is MeshType.Skinned)
        {
            throw new GMConverterException(
                $"MeshSet meshType={meshType} is not supported by the static-mesh reader yet. Skinned extraction is phase-2 work.");
        }

        var lods = new List<FrostbiteMeshLod>(lodCount);
        for (var i = 0; i < lodCount; i++)
        {
            lods.Add(ReadLod(blob, i, (int)lodOffsets[i]));
        }

        return new FrostbiteMeshSet(
            Name: ReadString(blob, nameOffset),
            ShortName: ReadString(blob, fullnameOffset),
            BoundingBoxMin: bboxMin,
            BoundingBoxMax: bboxMax,
            MeshType: meshType,
            Flags: flags,
            Lods: lods);
    }

    private static FrostbiteMeshLod ReadLod(ReadOnlySpan<byte> blob, int lodIndex, int offset)
    {
        var meshType = (MeshType)BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(offset + 0, 4));
        // +4 MaxInstances.
        var sectionCount = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(offset + 8, 4));
        var sectionOffset = (int)BinaryPrimitives.ReadInt64LittleEndian(blob.Slice(offset + 12, 8));
        // +20..+80 SubsetCategories[5] × (int + long). Each entry is a list of section indices
        // (one byte each) categorized by render pass: 0=Opaque, 1=Transparent,
        // 2=TransparentDecal, 3=ZOnly, 4=Shadow. Only the first three are visible — ZOnly /
        // Shadow sections are depth-only proxies that we must NOT render as geometry (they
        // overlap the real body with no UVs and would show as untextured white otherwise).
        var renderableSectionIndices = new HashSet<int>();
        for (var c = 0; c < 5; c++)
        {
            var catCount = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(offset + 20 + c * 12, 4));
            var catOffset = (int)BinaryPrimitives.ReadInt64LittleEndian(blob.Slice(offset + 20 + c * 12 + 4, 8));
            if (catCount == 0 || c >= 3)
            {
                continue;
            }
            for (var z = 0; z < catCount; z++)
            {
                renderableSectionIndices.Add(blob[catOffset + z]);
            }
        }
        var lodFlags = BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(offset + 80, 4));
        var indexBufferFormat = (IndexBufferFormat)BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(offset + 84, 4));
        var indexBufferSize = BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(offset + 88, 4));
        var vertexBufferSize = BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(offset + 92, 4));
        var adjacencyBufferSize = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(offset + 96, 4));
        var chunkId = ReadGuid(blob, offset + 100);
        var inlineDataOffset = BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(offset + 116, 4));
        // +120..+128 AdjacencyBufferOffset.
        // +128..+136 stringOffset01 (shader debug name — not surfaced).
        var lodNameOffset = BinaryPrimitives.ReadInt64LittleEndian(blob.Slice(offset + 136, 8));
        var lodShortNameOffset = BinaryPrimitives.ReadInt64LittleEndian(blob.Slice(offset + 144, 8));
        // +152..+156 NameHash, +156..+164 unknownLong, +164..+176 Pad(16).
        _ = lodIndex;

        if (sectionCount < 0)
        {
            throw new GMConverterException(
                $"MeshSet LOD record at offset {offset} declared a negative section count ({sectionCount}). Header offsets are misaligned against this asset's layout.");
        }

        var sections = new List<FrostbiteMeshSection>(sectionCount);
        for (var i = 0; i < sectionCount; i++)
        {
            sections.Add(ReadSection(blob, sectionOffset + i * _sectionStride));
        }

        return new FrostbiteMeshLod(
            LodIndex: lodIndex,
            MeshType: meshType,
            Flags: lodFlags,
            IndexBufferFormat: indexBufferFormat,
            IndexBufferSize: indexBufferSize,
            VertexBufferSize: vertexBufferSize,
            AdjacencyBufferSize: adjacencyBufferSize,
            ChunkId: chunkId,
            InlineDataOffset: inlineDataOffset,
            Name: ReadString(blob, lodNameOffset),
            ShortName: ReadString(blob, lodShortNameOffset),
            Sections: sections,
            RenderableSectionIndices: renderableSectionIndices);
    }

    private static FrostbiteMeshSection ReadSection(ReadOnlySpan<byte> blob, int offset)
    {
        // +0..+16 Offset1, Offset2 (runtime pointers, always 0 on disk).
        var materialNameOffset = BinaryPrimitives.ReadInt64LittleEndian(blob.Slice(offset + 16, 8));
        var materialId = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(offset + 24, 4));
        // +28..+32 LightMapUvMappingIndex.
        var primitiveCount = BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(offset + 32, 4));
        var startIndex = BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(offset + 36, 4));
        var vertexOffset = BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(offset + 40, 4));
        var vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(offset + 44, 4));
        var vertexStride = blob[offset + 48];
        var primitiveType = (PrimitiveType)blob[offset + 49];
        // +50..+52 placeholder BonesPerVertex / boneCount, overwritten by the SWS path below.
        // +52..+64 three unknown uints. +64..+65 BonesPerVertex (real). +65..+66 pad. +66..+68 boneCount (ushort).
        var bonesPerVertex = blob[offset + 64];
        var boneCount = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(offset + 66, 2));
        // +68..+76 BoneListOffset, +76..+84 unknownULong.

        var decl0 = ReadDeclaration(blob, offset + 84);
        var decl1 = ReadDeclaration(blob, offset + 84 + _declarationStride);
        // TexCoordRatios at +84+200=+284: 6 × float. Used to undo the per-section UV scaling
        // applied to Half2 / packed UVs at vertex-stream encode time. Without these the UVs
        // come out compressed against (0,0)—(1,1) and textures appear stretched / wrong.
        var texCoordRatiosOffset = offset + 84 + _declarationStride * 2;
        var texCoordRatios = new float[6];
        for (var i = 0; i < 6; i++)
        {
            texCoordRatios[i] = BinaryPrimitives.ReadSingleLittleEndian(blob.Slice(texCoordRatiosOffset + i * 4, 4));
        }

        return new FrostbiteMeshSection(
            MaterialName: ReadString(blob, materialNameOffset),
            MaterialId: materialId,
            PrimitiveCount: primitiveCount,
            StartIndex: startIndex,
            VertexOffset: vertexOffset,
            VertexCount: vertexCount,
            VertexStride: vertexStride,
            PrimitiveType: primitiveType,
            BonesPerVertex: bonesPerVertex,
            BoneCount: boneCount,
            Declarations: [decl0, decl1],
            TexCoordRatios: texCoordRatios);
    }

    private static FrostbiteVertexDeclaration ReadDeclaration(ReadOnlySpan<byte> blob, int offset)
    {
        // Record layout: Elements[_maxElements] × 4B, Streams[_maxStreams] × 2B,
        // ElementCount(1), StreamCount(1), pad(2). The on-disk record always reserves space for
        // the max element / stream count even when the live ElementCount / StreamCount are
        // smaller; we read only up to the declared counts.
        const int elementsBlock = _maxElements * 4;
        const int streamsBlock = _maxStreams * 2;
        var elementCount = blob[offset + elementsBlock + streamsBlock];
        var streamCount = blob[offset + elementsBlock + streamsBlock + 1];

        if (elementCount > _maxElements || streamCount > _maxStreams)
        {
            throw new GMConverterException(
                $"MeshSet GeometryDeclarationDesc declares ElementCount={elementCount} or StreamCount={streamCount} beyond the engine's static limits ({_maxElements}/{_maxStreams}). Blob is malformed.");
        }

        var elements = new FrostbiteVertexElement[elementCount];
        for (var i = 0; i < elementCount; i++)
        {
            var slice = blob.Slice(offset + i * 4, 4);
            elements[i] = new FrostbiteVertexElement(
                Usage: (VertexElementUsage)slice[0],
                Format: (VertexElementFormat)slice[1],
                Offset: slice[2],
                StreamIndex: slice[3]);
        }

        var streams = new FrostbiteVertexStream[streamCount];
        for (var i = 0; i < streamCount; i++)
        {
            var slice = blob.Slice(offset + elementsBlock + i * 2, 2);
            streams[i] = new FrostbiteVertexStream(
                VertexStride: slice[0],
                Classification: (VertexElementClassification)slice[1]);
        }

        return new FrostbiteVertexDeclaration(elements, streams);
    }

    private static Vector3 ReadVec3(ReadOnlySpan<byte> blob, int offset)
    {
        return new Vector3(
            BinaryPrimitives.ReadSingleLittleEndian(blob.Slice(offset, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(blob.Slice(offset + 4, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(blob.Slice(offset + 8, 4)));
    }

    private static Guid ReadGuid(ReadOnlySpan<byte> blob, int offset)
    {
        return new Guid(blob.Slice(offset, 16));
    }

    private static string ReadString(ReadOnlySpan<byte> blob, long absoluteOffset)
    {
        if (absoluteOffset <= 0 || absoluteOffset >= blob.Length)
        {
            return string.Empty;
        }

        var start = (int)absoluteOffset;
        var end = start;
        while (end < blob.Length && blob[end] != 0)
        {
            end++;
        }

        return end == start ? string.Empty : Encoding.UTF8.GetString(blob.Slice(start, end - start));
    }
}
