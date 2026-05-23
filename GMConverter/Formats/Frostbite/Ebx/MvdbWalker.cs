using System.Buffers.Binary;
using System.Text;
using GMConverter.Common;

namespace GMConverter.Formats.Frostbite.Ebx;

// Walks a MeshVariationDatabase EBX blob (SWS / SWBF II legacy V2 format) and extracts the
// resolved (Mesh GUID, MaterialIndex, ParameterName, Texture GUID) tuples. The mesh and
// texture GUIDs are partition GUIDs — they match EbxAssetEntry.Guid via FrostySdk's lookup.
//
// We rely on the EBX file being self-describing (header has named field descriptors), so we
// don't need TypeLibrary or per-game SDK DLLs to walk these. Types we walk by name:
//   MeshVariationDatabase  →  _Entries (array)
//   MeshVariationDatabaseEntry  →  _Mesh (PointerRef), _Materials (array)
//   MeshVariationDatabaseMaterial  →  _TextureParameters (array)
//   TextureShaderParameter  →  _Value (PointerRef), _ParameterName (CString)
internal static class MvdbWalker
{
    public static IReadOnlyList<MvdbMeshBinding> Walk(ReadOnlySpan<byte> blob)
    {
        var header = EbxLegacyHeader.Read(blob);
        var dataStart = (int)header.StringsOffset + (int)header.StringTableLength;

        // Find each TypeDescriptor we care about by name.
        var mvdbType = FindType(header, "MeshVariationDatabase");
        var mvdbEntryType = FindType(header, "MeshVariationDatabaseEntry");
        var mvdbMaterialType = FindType(header, "MeshVariationDatabaseMaterial");
        var textureParamType = FindType(header, "TextureShaderParameter");
        if (mvdbType is null || mvdbEntryType is null || mvdbMaterialType is null || textureParamType is null)
        {
            return [];
        }

        // The MVDB EBX has exactly one root instance of type MeshVariationDatabase.
        // Walk the file from dataStart, jumping to each instance's offset.
        var pos = dataStart;
        var bindings = new List<MvdbMeshBinding>();

        for (var i = 0; i < header.Instances.Length; i++)
        {
            var inst = header.Instances[i];
            var type = ResolveType(header, inst.TypeDescriptorRef);

            for (var c = 0; c < inst.Count; c++)
            {
                // Align to the type's natural alignment, then skip the exported instance GUID
                // prefix (16 bytes) for exported instances. Non-exported instances still have
                // an 8-byte prefix when alignment != 4 (per v2 LegacyEbx.ReadObjects).
                pos = AlignUp(pos, type.Alignment);
                if (inst.IsExported)
                {
                    pos += 16;
                }
                else if (type.Alignment != 4)
                {
                    pos += 8;
                }

                var instanceStart = pos - 8; // v2 sets stream.Position to (pos - 8) before ReadType

                if (type.NameHash == mvdbType.NameHash)
                {
                    WalkMvdb(blob, header, instanceStart, mvdbEntryType, mvdbMaterialType, textureParamType, bindings);
                }

                pos = instanceStart + type.Size;
            }
        }

        return bindings;
    }

    private static void WalkMvdb(
        ReadOnlySpan<byte> blob,
        EbxLegacyHeader header,
        int instanceStart,
        EbxLegacyTypeDescriptor entryType,
        EbxLegacyTypeDescriptor materialType,
        EbxLegacyTypeDescriptor paramType,
        List<MvdbMeshBinding> bindings)
    {
        var mvdbType = ResolveTypeByName(header, "MeshVariationDatabase")!;
        var entriesField = FindField(header, mvdbType, "Entries");
        if (entriesField is null)
        {
            return;
        }

        var entriesArray = ReadArrayHandle(blob, instanceStart + (int)entriesField.DataOffset, header);
        if (entriesArray.Count == 0)
        {
            return;
        }

        // Each MVDB entry references one Mesh + a list of Materials.
        for (var i = 0; i < entriesArray.Count; i++)
        {
            var entryOffset = (int)header.ArrayOffset + (int)entriesArray.Offset + i * entryType.Size;

            var meshField = FindField(header, entryType, "Mesh");
            var materialsField = FindField(header, entryType, "Materials");
            if (meshField is null || materialsField is null)
            {
                continue;
            }

            var meshGuid = ReadPointerRefAsImportPartition(blob, entryOffset + (int)meshField.DataOffset, header);
            if (meshGuid == Guid.Empty)
            {
                continue;
            }

            var materialBindings = new List<MvdbMaterialBinding>();
            var materialsArray = ReadArrayHandle(blob, entryOffset + (int)materialsField.DataOffset, header);
            for (var m = 0; m < materialsArray.Count; m++)
            {
                var materialOffset = (int)header.ArrayOffset + (int)materialsArray.Offset + m * materialType.Size;

                var texParamsField = FindField(header, materialType, "TextureParameters");
                if (texParamsField is null)
                {
                    materialBindings.Add(new MvdbMaterialBinding(m, []));
                    continue;
                }

                var texParamsArray = ReadArrayHandle(blob, materialOffset + (int)texParamsField.DataOffset, header);
                var paramBindings = new List<MvdbTextureBinding>();
                for (var p = 0; p < texParamsArray.Count; p++)
                {
                    var paramOffset = (int)header.ArrayOffset + (int)texParamsArray.Offset + p * paramType.Size;

                    var valueField = FindField(header, paramType, "Value");
                    var nameField = FindField(header, paramType, "ParameterName");
                    if (valueField is null || nameField is null)
                    {
                        continue;
                    }

                    var textureGuid = ReadPointerRefAsImportPartition(blob, paramOffset + (int)valueField.DataOffset, header);
                    var paramName = ReadCString(blob, paramOffset + (int)nameField.DataOffset, header);
                    if (textureGuid != Guid.Empty && !string.IsNullOrEmpty(paramName))
                    {
                        paramBindings.Add(new MvdbTextureBinding(paramName, textureGuid));
                    }
                }

                materialBindings.Add(new MvdbMaterialBinding(m, paramBindings));
            }

            bindings.Add(new MvdbMeshBinding(meshGuid, materialBindings));
        }
    }

    // Looks up a TypeDescriptor by its name. Type names come from the EBX file's own name
    // table, so this works without TypeLibrary or any SDK DLL.
    private static EbxLegacyTypeDescriptor? FindType(EbxLegacyHeader header, string name)
    {
        foreach (var t in header.TypeDescriptors)
        {
            if (t.Name == name)
            {
                return t;
            }
        }
        return null;
    }

    private static EbxLegacyTypeDescriptor? ResolveTypeByName(EbxLegacyHeader header, string name) => FindType(header, name);

    private static EbxLegacyTypeDescriptor ResolveType(EbxLegacyHeader header, ushort typeRef)
    {
        if (typeRef >= header.TypeDescriptors.Length)
        {
            throw new GMConverterException($"EBX type ref {typeRef} out of range ({header.TypeDescriptors.Length}).");
        }
        return header.TypeDescriptors[typeRef];
    }

    private static EbxLegacyFieldDescriptor? FindField(EbxLegacyHeader header, EbxLegacyTypeDescriptor type, string name)
    {
        for (var i = 0; i < type.FieldCount; i++)
        {
            var f = header.FieldDescriptors[type.FieldIndex + i];
            if (f.Name == name)
            {
                return f;
            }
        }
        return null;
    }

    // Array fields in V2 EBX are int32 indices into the file's Arrays table. The table entry
    // says where the array data lives (offset within ArrayOffset region) and how many elements.
    private static (uint Offset, int Count) ReadArrayHandle(ReadOnlySpan<byte> blob, int fieldPos, EbxLegacyHeader header)
    {
        var idx = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(fieldPos, 4));
        if (idx < 0 || idx >= header.Arrays.Length)
        {
            return (0, 0);
        }
        var arr = header.Arrays[idx];
        return (arr.Offset, arr.Count);
    }

    // PointerRef in V2: uint32 packed. Top bit (0x80000000) = external import; index is in the
    // low 31 bits. We only care about external imports here (mesh/texture GUIDs live in the
    // import table). Returns Guid.Empty if the pointer is null or internal.
    private static Guid ReadPointerRefAsImportPartition(ReadOnlySpan<byte> blob, int fieldPos, EbxLegacyHeader header)
    {
        var raw = BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(fieldPos, 4));
        if ((raw >> 31) == 0)
        {
            return Guid.Empty;
        }
        var importIdx = (int)(raw & 0x7FFFFFFF);
        if (importIdx < 0 || importIdx >= header.Imports.Length)
        {
            return Guid.Empty;
        }
        return header.Imports[importIdx].PartitionGuid;
    }

    // CString in V2: uint32 offset into the string table (or 0xFFFFFFFF for empty).
    private static string ReadCString(ReadOnlySpan<byte> blob, int fieldPos, EbxLegacyHeader header)
    {
        var off = BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(fieldPos, 4));
        if (off == 0xFFFFFFFF)
        {
            return string.Empty;
        }
        var stringStart = (int)header.StringsOffset + (int)off;
        var end = stringStart;
        while (end < blob.Length && blob[end] != 0)
        {
            end++;
        }
        return Encoding.UTF8.GetString(blob[stringStart..end]);
    }

    private static int AlignUp(int v, int alignment) => alignment <= 1 ? v : (v + alignment - 1) & ~(alignment - 1);
}

internal sealed record MvdbMeshBinding(Guid MeshPartitionGuid, IReadOnlyList<MvdbMaterialBinding> Materials);

internal sealed record MvdbMaterialBinding(int MaterialIndex, IReadOnlyList<MvdbTextureBinding> Textures);

internal sealed record MvdbTextureBinding(string ParameterName, Guid TexturePartitionGuid);
