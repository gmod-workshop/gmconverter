using System.Buffers.Binary;
using System.Numerics;
using GMConverter.Common;

namespace GMConverter.Formats.Frostbite;

// Decodes a packed Frostbite vertex stream against a parsed GeometryDeclarationDesc into a flat
// list of (Position, Normal, UV) tuples. Only the element types observed in Squadrons static
// meshes are wired up: Half3 / Half4 / Float3 for positions, Half4 / Float3 / UByte4N for
// normals, Half2 / Float2 for UVs. Anything outside that surface throws so callers see exactly
// which format gap to fill.
internal static class FrostbiteVertexDecoder
{
    public static DecodedVertices Decode(
        ReadOnlySpan<byte> stream,
        FrostbiteMeshSection section)
    {
        var decl = section.Declarations[0];
        if (decl.Streams.Count == 0 || decl.Streams.Count > 1)
        {
            throw new GMConverterException(
                $"Frostbite vertex decoder only supports single-stream sections right now (decl had {decl.Streams.Count}).");
        }

        var stride = section.VertexStride;
        var vertexCount = (int)section.VertexCount;
        if (stream.Length < vertexCount * stride)
        {
            throw new GMConverterException(
                $"Vertex stream slice is {stream.Length} bytes, need {vertexCount * stride} for {vertexCount} vertices at stride {stride}.");
        }

        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var hasNormals = false;
        var hasUvs = false;

        // UVs are stored as Half2 in DirectX convention (V=0 at top, increases downward) —
        // matching the wavebend FrostyToolsuite fork's DirectX preview. glTF expects the SAME
        // DX convention, but our GLTFExporter applies a (1 - v) Y-flip to all imported UVs
        // because the other importers (OPT/MOW/PSK) feed it OpenGL-convention UVs. To get
        // glTF-correct UVs out the other side, we pre-flip here so the exporter's flip cancels
        // back to the original DX-V. TexCoordRatios is read for completeness but never
        // applied — empirically confirmed against the wavebend FBX exporter (which also skips
        // ratios) and the visual match between our decoded textures and Frosty's PNG dumps.
        // For diagnosis: also collect TexCoord1 so we can compare its range.
        var uvs1 = new Vector2[vertexCount];
        var hasUvs1 = false;

        for (var i = 0; i < vertexCount; i++)
        {
            var vertex = stream.Slice(i * stride, stride);

            foreach (var element in decl.Elements)
            {
                var slice = vertex[element.Offset..];

                switch (element.Usage)
                {
                    case VertexElementUsage.Pos:
                        positions[i] = ReadVec3(slice, element.Format);
                        break;
                    case VertexElementUsage.Normal:
                        normals[i] = ReadVec3(slice, element.Format);
                        hasNormals = true;
                        break;
                    case VertexElementUsage.TexCoord0:
                        var rawUv = ReadVec2(slice, element.Format);
                        uvs[i] = new Vector2(rawUv.X, 1f - rawUv.Y);
                        hasUvs = true;
                        break;
                    case VertexElementUsage.TexCoord1:
                        var rawUv1 = ReadVec2(slice, element.Format);
                        uvs1[i] = new Vector2(rawUv1.X, 1f - rawUv1.Y);
                        hasUvs1 = true;
                        break;
                    default:
                        // Other elements (BinormalSign, Tangent, RadiosityTexCoord, bone
                        // indices/weights) are not consumed by the static-mesh path. Skip.
                        break;
                }
            }
        }

        // Heuristic UV-channel selection: Frostbite vehicles route the diffuse UV through
        // TexCoord1 (TexCoord0 is the *decal* UV — intentionally tile-extended past [0,1] to
        // signal "no decal here"), while static props put the diffuse on TexCoord0. We don't
        // have the compiled shader, so we pick whichever channel best fits [0,1] across the
        // section's vertices. Awing body: TC0=[0,2.2] vs TC1=[0,1] → pick TC1. Mynock body:
        // TC0=[0,1] and TC1 may be lightmap padding → pick TC0.
        if (hasUvs1 && hasUvs && OutOfRangeBadness(uvs1) < OutOfRangeBadness(uvs))
        {
            uvs = uvs1;
        }

        if (!hasNormals)
        {
            Array.Fill(normals, Vector3.UnitZ);
        }
        if (!hasUvs)
        {
            Array.Fill(uvs, Vector2.Zero);
        }

        return new DecodedVertices(positions, normals, uvs);
    }

    // Returns the total "distance outside [0,1]" summed over all UVs. Used to compare which UV
    // channel is more likely to be the diffuse UV (the one the shader samples with REPEAT against
    // a [0,1]-laid-out texture). Smaller = better fit.
    private static float OutOfRangeBadness(Vector2[] uvs)
    {
        if (uvs.Length == 0) { return float.MaxValue; }
        float total = 0f;
        foreach (var uv in uvs)
        {
            total += Math.Max(0, uv.X - 1f) + Math.Max(0, -uv.X);
            total += Math.Max(0, uv.Y - 1f) + Math.Max(0, -uv.Y);
        }
        return total;
    }

    private static Vector3 ReadVec3(ReadOnlySpan<byte> slice, VertexElementFormat format)
    {
        return format switch
        {
            VertexElementFormat.Float3 or VertexElementFormat.Float4 => new Vector3(
                BinaryPrimitives.ReadSingleLittleEndian(slice[..4]),
                BinaryPrimitives.ReadSingleLittleEndian(slice.Slice(4, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(slice.Slice(8, 4))),
            VertexElementFormat.Half3 or VertexElementFormat.Half4 => new Vector3(
                (float)BinaryPrimitives.ReadHalfLittleEndian(slice[..2]),
                (float)BinaryPrimitives.ReadHalfLittleEndian(slice.Slice(2, 2)),
                (float)BinaryPrimitives.ReadHalfLittleEndian(slice.Slice(4, 2))),
            VertexElementFormat.UByte4N => new Vector3(
                slice[0] / 255f * 2f - 1f,
                slice[1] / 255f * 2f - 1f,
                slice[2] / 255f * 2f - 1f),
            VertexElementFormat.Byte4N => new Vector3(
                (sbyte)slice[0] / 127f,
                (sbyte)slice[1] / 127f,
                (sbyte)slice[2] / 127f),
            _ => throw new GMConverterException(
                $"Frostbite vertex decoder cannot read a Vector3 from format {format}; extend the switch when this case appears.")
        };
    }

    private static Vector2 ReadVec2(ReadOnlySpan<byte> slice, VertexElementFormat format)
    {
        return format switch
        {
            VertexElementFormat.Float2 or VertexElementFormat.Float3 or VertexElementFormat.Float4 => new Vector2(
                BinaryPrimitives.ReadSingleLittleEndian(slice[..4]),
                BinaryPrimitives.ReadSingleLittleEndian(slice.Slice(4, 4))),
            VertexElementFormat.Half2 or VertexElementFormat.Half3 or VertexElementFormat.Half4 => new Vector2(
                (float)BinaryPrimitives.ReadHalfLittleEndian(slice[..2]),
                (float)BinaryPrimitives.ReadHalfLittleEndian(slice.Slice(2, 2))),
            _ => throw new GMConverterException(
                $"Frostbite vertex decoder cannot read a Vector2 from format {format}; extend the switch when this case appears.")
        };
    }

    internal sealed record DecodedVertices(
        IReadOnlyList<Vector3> Positions,
        IReadOnlyList<Vector3> Normals,
        IReadOnlyList<Vector2> Uvs);
}
