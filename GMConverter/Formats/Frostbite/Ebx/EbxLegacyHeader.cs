using System.Buffers.Binary;
using System.Text;
using GMConverter.Common;

namespace GMConverter.Formats.Frostbite.Ebx;

// Minimal port of FrostyToolsuite v2's LegacyEbx.EbxHeader (and its companion descriptor
// structs) for SWS / SWBF II's EBX v4 files. Unlike the RiffEbx path used by newer Frostbite
// games, V2 EBX stores the field descriptors (with name + DataOffset + flags + type ref)
// directly in the file header. That means we can walk arbitrary EBX instances by NAME without
// ever loading a TypeLibrary or the per-game SDK DLLs — the EBX file is self-describing.
//
// This reader is deliberately minimal: it parses the header and produces the descriptor
// tables. Higher-level walkers (see MvdbWalker) handle the actual struct traversal.
internal sealed class EbxLegacyHeader
{
    public uint StringsOffset { get; init; }
    public uint StringTableLength { get; init; }
    public uint ArrayOffset { get; init; }
    public Guid PartitionGuid { get; init; }
    public required EbxImportRef[] Imports { get; init; }
    public required EbxLegacyFieldDescriptor[] FieldDescriptors { get; init; }
    public required EbxLegacyTypeDescriptor[] TypeDescriptors { get; init; }
    public required EbxLegacyInstance[] Instances { get; init; }
    public required EbxLegacyArray[] Arrays { get; init; }
    public ushort ExportedInstanceCount { get; init; }

    public static EbxLegacyHeader Read(ReadOnlySpan<byte> blob)
    {
        var p = 0;

        var magic = ReadU32(blob, ref p);
        if (magic != 0x0FB2D1CE && magic != 0x0FB4D1CE)
        {
            throw new GMConverterException($"Unsupported EBX magic 0x{magic:X8}; expected V1 or V4.");
        }
        var isV4 = magic == 0x0FB4D1CE;

        var stringsOffset = ReadU32(blob, ref p);
        _ = ReadU32(blob, ref p); // StringsAndDataLength
        var importCount = ReadI32(blob, ref p);
        var instanceCount = ReadU16(blob, ref p);
        var exportedInstanceCount = ReadU16(blob, ref p);
        _ = ReadU16(blob, ref p); // UniqueTypeCount
        var typeDescriptorCount = ReadU16(blob, ref p);
        var fieldDescriptorCount = ReadU16(blob, ref p);
        var typeNameTableLength = ReadU16(blob, ref p);
        var stringTableLength = ReadU32(blob, ref p);
        var arrayCount = ReadI32(blob, ref p);
        var dataLength = ReadU32(blob, ref p);
        var partitionGuid = ReadGuid(blob, ref p);

        var arrayOffset = stringsOffset + stringTableLength + dataLength;

        if (isV4)
        {
            _ = ReadI32(blob, ref p); // BoxedValueCount
            _ = ReadU32(blob, ref p); // BoxedValueOffset
        }
        else
        {
            Pad(ref p, 16);
        }

        var imports = new EbxImportRef[importCount];
        for (var i = 0; i < importCount; i++)
        {
            var partition = ReadGuid(blob, ref p);
            var instance = ReadGuid(blob, ref p);
            imports[i] = new EbxImportRef(partition, instance);
        }

        // Type-name table — null-terminated UTF-8 strings hashed by Frostbite's standard string hash
        // (FNV-1 derivative). We need the hash function later to look up names by hash.
        var typeNames = new Dictionary<int, string>();
        var typeNamesEnd = p + typeNameTableLength;
        while (p < typeNamesEnd)
        {
            var start = p;
            while (p < blob.Length && blob[p] != 0)
            {
                p++;
            }
            if (p > start)
            {
                var name = Encoding.UTF8.GetString(blob[start..p]);
                typeNames.TryAdd(HashString(name), name);
            }
            p++; // skip null terminator
        }

        var fieldDescriptors = new EbxLegacyFieldDescriptor[fieldDescriptorCount];
        for (var i = 0; i < fieldDescriptorCount; i++)
        {
            var nameHash = ReadU32(blob, ref p);
            var flags = ReadU16(blob, ref p);
            var typeRef = ReadU16(blob, ref p);
            var dataOffset = ReadU32(blob, ref p);
            _ = ReadU32(blob, ref p); // SecondOffset
            var name = typeNames.GetValueOrDefault((int)nameHash, string.Empty);
            fieldDescriptors[i] = new EbxLegacyFieldDescriptor(name, nameHash, flags, typeRef, dataOffset);
        }

        var typeDescriptors = new EbxLegacyTypeDescriptor[typeDescriptorCount];
        for (var i = 0; i < typeDescriptorCount; i++)
        {
            var nameHash = ReadU32(blob, ref p);
            var fieldIndex = ReadI32(blob, ref p);
            var fieldCount = blob[p++];
            var alignment = blob[p++];
            var flags = ReadU16(blob, ref p);
            var size = ReadU16(blob, ref p);
            _ = ReadU16(blob, ref p); // SecondSize
            var name = typeNames.GetValueOrDefault((int)nameHash, string.Empty);
            // FieldCount high bit overflow lives in alignment's top bit (per v2 source).
            var realFieldCount = (ushort)(fieldCount | ((alignment & 0x80) << 1));
            var realAlignment = (byte)(alignment & 0x7F);
            typeDescriptors[i] = new EbxLegacyTypeDescriptor(name, nameHash, fieldIndex, realFieldCount, realAlignment, flags, size);
        }

        var instances = new EbxLegacyInstance[instanceCount];
        for (var i = 0; i < instanceCount; i++)
        {
            var typeRef = ReadU16(blob, ref p);
            var count = ReadU16(blob, ref p);
            instances[i] = new EbxLegacyInstance(typeRef, count, i < exportedInstanceCount);
        }
        Pad(ref p, 16);

        var arrays = new EbxLegacyArray[arrayCount];
        for (var i = 0; i < arrayCount; i++)
        {
            var off = ReadU32(blob, ref p);
            var cnt = ReadI32(blob, ref p);
            var aTypeRef = ReadI32(blob, ref p);
            arrays[i] = new EbxLegacyArray(off, cnt, aTypeRef);
        }

        return new EbxLegacyHeader
        {
            StringsOffset = stringsOffset,
            StringTableLength = stringTableLength,
            ArrayOffset = arrayOffset,
            PartitionGuid = partitionGuid,
            Imports = imports,
            FieldDescriptors = fieldDescriptors,
            TypeDescriptors = typeDescriptors,
            Instances = instances,
            Arrays = arrays,
            ExportedInstanceCount = exportedInstanceCount,
        };
    }

    private static uint ReadU32(ReadOnlySpan<byte> b, ref int p)
    {
        var v = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(p, 4));
        p += 4;
        return v;
    }

    private static int ReadI32(ReadOnlySpan<byte> b, ref int p)
    {
        var v = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(p, 4));
        p += 4;
        return v;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> b, ref int p)
    {
        var v = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(p, 2));
        p += 2;
        return v;
    }

    private static Guid ReadGuid(ReadOnlySpan<byte> b, ref int p)
    {
        var g = new Guid(b.Slice(p, 16));
        p += 16;
        return g;
    }

    private static void Pad(ref int p, int alignment)
    {
        var rem = p % alignment;
        if (rem != 0)
        {
            p += alignment - rem;
        }
    }

    // Frostbite's string hash — DJB2 variant (offset 5381, prime 33). Used by EBX to key
    // type / field names in the file's name table. Matches Frosty.Sdk.Utils.HashString.
    public static int HashString(string value)
    {
        const uint kOffset = 5381;
        const uint kPrime = 33;
        var hash = kOffset;
        foreach (var c in value)
        {
            hash = (hash * kPrime) ^ (byte)c;
        }
        return (int)hash;
    }
}

internal sealed record EbxImportRef(Guid PartitionGuid, Guid InstanceGuid);

internal sealed record EbxLegacyFieldDescriptor(
    string Name,
    uint NameHash,
    ushort Flags,
    ushort TypeDescriptorRef,
    uint DataOffset);

internal sealed record EbxLegacyTypeDescriptor(
    string Name,
    uint NameHash,
    int FieldIndex,
    ushort FieldCount,
    byte Alignment,
    ushort Flags,
    ushort Size);

internal sealed record EbxLegacyInstance(
    ushort TypeDescriptorRef,
    ushort Count,
    bool IsExported);

internal sealed record EbxLegacyArray(uint Offset, int Count, int TypeDescriptorRef);
