using System.Buffers.Binary;
using System.Text;

namespace GMConverter.Formats.Frostbite.Ebx;

// Walks a SurfaceShaderPreset (SSP) EBX blob and extracts its TextureParameters — pairs of
// (ParameterName, TexturePartitionGuid) that bind shader inputs (BaseColor / Normal / Emissive)
// to actual TextureAsset EBX entries.
//
// SSP is the authoritative *default* texture set for a mesh material; we read it instead of
// guessing through MeshVariationDatabase, which is only meaningful for cosmetic skin variations.
// FrostyEditor's mesh editor reads `material.Shader.TextureParameters` first and only falls back
// to MVDB when the SSP comes up empty (see FrostyMeshSetEditor.cs around line 540, 945).
//
// SSP layout (V2 EBX, names per the exported XML):
//   SurfaceShaderPreset
//     ShaderPreset (inline struct: SurfaceShaderInstanceDataStruct)
//       TextureParameters (array<TextureShaderParameter>)
//         Value : PointerRef → TextureAsset
//         ParameterName : CString
internal static class SspWalker
{
    public static IReadOnlyList<MvdbTextureBinding> ReadTextureParameters(ReadOnlySpan<byte> blob)
    {
        var header = EbxLegacyHeader.Read(blob);
        var dataStart = (int)header.StringsOffset + (int)header.StringTableLength;

        var sspType = FindType(header, "SurfaceShaderPreset");
        var shaderStructType = FindType(header, "SurfaceShaderInstanceDataStruct");
        var paramType = FindType(header, "TextureShaderParameter");
        if (sspType is null || shaderStructType is null || paramType is null)
        {
            return [];
        }

        var bindings = new List<MvdbTextureBinding>();
        var pos = dataStart;

        for (var i = 0; i < header.Instances.Length; i++)
        {
            var inst = header.Instances[i];
            var type = header.TypeDescriptors[inst.TypeDescriptorRef];

            for (var c = 0; c < inst.Count; c++)
            {
                pos = AlignUp(pos, type.Alignment);
                if (inst.IsExported)
                {
                    pos += 16;
                }
                else if (type.Alignment != 4)
                {
                    pos += 8;
                }
                var instanceStart = pos - 8;

                if (type.NameHash == sspType.NameHash)
                {
                    WalkSsp(blob, header, instanceStart, sspType, shaderStructType, paramType, bindings);
                }

                pos = instanceStart + type.Size;
            }
        }

        return bindings;
    }

    private static void WalkSsp(
        ReadOnlySpan<byte> blob,
        EbxLegacyHeader header,
        int instanceStart,
        EbxLegacyTypeDescriptor sspType,
        EbxLegacyTypeDescriptor shaderStructType,
        EbxLegacyTypeDescriptor paramType,
        List<MvdbTextureBinding> bindings)
    {
        var shaderPresetField = FindField(header, sspType, "ShaderPreset");
        if (shaderPresetField is null)
        {
            return;
        }

        // ShaderPreset is an INLINE struct, so its data sits directly at instanceStart + offset.
        var structStart = instanceStart + (int)shaderPresetField.DataOffset;
        var texParamsField = FindField(header, shaderStructType, "TextureParameters");
        if (texParamsField is null)
        {
            return;
        }

        var arr = ReadArrayHandle(blob, structStart + (int)texParamsField.DataOffset, header);
        for (var i = 0; i < arr.Count; i++)
        {
            var paramOffset = (int)header.ArrayOffset + (int)arr.Offset + i * paramType.Size;
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
                bindings.Add(new MvdbTextureBinding(paramName, textureGuid));
            }
        }
    }

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

    private static (uint Offset, int Count) ReadArrayHandle(ReadOnlySpan<byte> blob, int fieldPos, EbxLegacyHeader header)
    {
        var idx = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(fieldPos, 4));
        if (idx < 0 || idx >= header.Arrays.Length)
        {
            return (0, 0);
        }
        var a = header.Arrays[idx];
        return (a.Offset, a.Count);
    }

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
