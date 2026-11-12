using System.Buffers.Binary;
using System.Numerics;
using GMConverter.Godot.Resources;

namespace GMConverter.Godot.Geometry;

/// <summary>
/// Decodes the surfaces of a Godot 4 <c>ArrayMesh</c>. A surface keeps three buffers: the vertex
/// buffer holds positions, normals and tangents; the attribute buffer holds each vertex's colour,
/// texture coordinates and custom data; the skin buffer holds each vertex's bones and weights.
/// Before Godot 4.2 (mesh format version 1) the vertex buffer interleaves each vertex's position,
/// normal and tangent; since then (version 2) it holds every position, then every normal and
/// tangent. Version 2 surfaces can be compressed: positions become 16-bit values within the
/// surface's bounds, texture coordinates 16-bit values, and the normal and tangent one rotation,
/// its axis where the normal was and its angle in the position's fourth component.
/// </summary>
internal static class GodotSurfaceReader
{
    private const long _vertex = 1 << 0;
    private const long _normal = 1 << 1;
    private const long _tangent = 1 << 2;
    private const long _color = 1 << 3;
    private const long _uv = 1 << 4;
    private const long _uv2 = 1 << 5;
    private const long _bones = 1 << 10;
    private const long _weights = 1 << 11;
    private const long _index = 1 << 12;
    private const int _customBase = 13;
    private const int _customBits = 3;
    private const long _use2dVertices = 1 << 25;
    private const long _use8BoneWeights = 1 << 27;
    private const long _compressAttributes = 1 << 29;
    private const long _formatVersion2 = 1L << 35;
    private const int _trianglesPrimitive = 3;

    /// <summary>The triangle surfaces of <paramref name="mesh"/>; points, lines and strips are left out.</summary>
    public static IEnumerable<GodotSurface> Read(GodotResource mesh)
    {
        if (mesh.Get<List<object?>>("_surfaces") is not { } surfaces)
        {
            yield break;
        }

        foreach (var entry in surfaces.OfType<Dictionary<object, object?>>())
        {
            if (Get<long>(entry, "primitive") == _trianglesPrimitive && Read(entry) is { } surface)
            {
                yield return surface;
            }
        }
    }

    private static GodotSurface? Read(Dictionary<object, object?> surface)
    {
        var format = Get<long>(surface, "format");
        var count = (int)Get<long>(surface, "vertex_count");
        if ((format & _vertex) == 0 || count == 0)
        {
            return null;
        }

        if ((format & _use2dVertices) != 0)
        {
            return null;
        }

        var interleaved = (format & _formatVersion2) == 0;
        var compressed = !interleaved && (format & _compressAttributes) != 0;
        var aabb = Get<GodotAabb>(surface, "aabb");
        var uvScale = surface.GetValueOrDefault("uv_scale") is Vector4 scale ? scale : Vector4.Zero;
        var vertexData = Get<byte[]>(surface, "vertex_data") ?? [];
        var attributeData = Get<byte[]>(surface, "attribute_data") ?? [];
        var skinData = Get<byte[]>(surface, "skin_data") ?? [];

        // Vertex buffer: positions, then normals and tangents, or each vertex's together.
        var hasNormal = (format & _normal) != 0;
        var hasTangent = (format & _tangent) != 0;
        var positionSize = compressed ? 8 : 12;
        var normalSize = compressed ? 4 : (hasNormal ? 4 : 0) + (hasTangent ? 4 : 0);
        var positionStride = interleaved ? positionSize + normalSize : positionSize;
        var normalStart = interleaved ? positionSize : count * positionSize;
        var normalStride = interleaved ? positionStride : normalSize;

        var positions = new Vector3[count];
        var normals = hasNormal ? new Vector3[count] : null;
        for (var vertex = 0; vertex < count; vertex++)
        {
            var offset = vertex * positionStride;
            positions[vertex] = compressed
                ? aabb.Position + (new Vector3(Unorm16(vertexData, offset), Unorm16(vertexData, offset + 2), Unorm16(vertexData, offset + 4)) * aabb.Size)
                : new Vector3(Float(vertexData, offset), Float(vertexData, offset + 4), Float(vertexData, offset + 8));

            if (normals is not null)
            {
                var normalOffset = normalStart + (vertex * normalStride);
                var octahedron = OctahedronDecode(Unorm16(vertexData, normalOffset), Unorm16(vertexData, normalOffset + 2));
                normals[vertex] = compressed && hasTangent
                    ? AxisAngleNormal(octahedron, Unorm16(vertexData, offset + 6))
                    : octahedron;
            }
        }

        // Attribute buffer, interleaved per vertex.
        var uvSize = compressed ? 4 : 8;
        var attributeStride = ((format & _color) != 0 ? 4 : 0) + ((format & _uv) != 0 ? uvSize : 0) + ((format & _uv2) != 0 ? uvSize : 0) + CustomSize(format);
        Vector4[]? colors = (format & _color) != 0 ? new Vector4[count] : null;
        Vector2[]? uvs = (format & _uv) != 0 ? new Vector2[count] : null;
        for (var vertex = 0; vertex < count && attributeStride > 0; vertex++)
        {
            var offset = vertex * attributeStride;
            if (colors is not null)
            {
                colors[vertex] = new Vector4(attributeData[offset], attributeData[offset + 1], attributeData[offset + 2], attributeData[offset + 3]) / 255f;
                offset += 4;
            }

            uvs?[vertex] = compressed
                ? DecompressUv(new Vector2(Unorm16(attributeData, offset), Unorm16(attributeData, offset + 2)), new Vector2(uvScale.X, uvScale.Y))
                : new Vector2(Float(attributeData, offset), Float(attributeData, offset + 4));
        }

        // Skin buffer: each vertex's bones, then its weights.
        int[]? bones = null;
        float[]? weights = null;
        var influences = (format & _use8BoneWeights) != 0 ? 8 : 4;
        if ((format & _bones) != 0 && (format & _weights) != 0 && skinData.Length >= count * influences * 4)
        {
            bones = new int[count * influences];
            weights = new float[count * influences];
            for (var vertex = 0; vertex < count; vertex++)
            {
                var offset = vertex * influences * 4;
                for (var slot = 0; slot < influences; slot++)
                {
                    bones[(vertex * influences) + slot] = BinaryPrimitives.ReadUInt16LittleEndian(skinData.AsSpan(offset + (slot * 2)));
                    weights[(vertex * influences) + slot] = Unorm16(skinData, offset + (influences * 2) + (slot * 2));
                }
            }
        }

        return new GodotSurface(
            surface.GetValueOrDefault("name") as string ?? string.Empty,
            positions,
            normals,
            uvs,
            colors,
            bones,
            weights,
            influences,
            ReadIndices(surface, format, count),
            surface.GetValueOrDefault("material") as GodotResource);
    }

    private static int[] ReadIndices(Dictionary<object, object?> surface, long format, int vertexCount)
    {
        if ((format & _index) == 0 || Get<byte[]>(surface, "index_data") is not { } data)
        {
            return [.. Enumerable.Range(0, vertexCount - (vertexCount % 3))];
        }

        var count = (int)Get<long>(surface, "index_count");
        var size = count > 0 ? data.Length / count : 2;
        var indices = new int[count];
        for (var index = 0; index < count; index++)
        {
            indices[index] = size == 4
                ? (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(index * 4))
                : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(index * 2));
        }

        return indices;
    }

    // Godot packs a unit vector into two values: the point on an octahedron, folded flat.
    internal static Vector3 OctahedronDecode(float x, float y)
    {
        var f = new Vector2((x * 2f) - 1f, (y * 2f) - 1f);
        var n = new Vector3(f.X, f.Y, 1f - MathF.Abs(f.X) - MathF.Abs(f.Y));
        var t = Math.Clamp(-n.Z, 0f, 1f);
        n.X += n.X >= 0f ? -t : t;
        n.Y += n.Y >= 0f ? -t : t;
        return Vector3.Normalize(n);
    }

    // A compressed surface's tangent frame is a rotation: an axis, and an angle from 0.5 (none) out
    // to 0 or 1 (half a turn) whose side gives the binormal's sign. The normal is the rotation's
    // third row, as Godot's shaders rebuild it.
    internal static Vector3 AxisAngleNormal(Vector3 axis, float angle)
    {
        var radians = MathF.Abs((angle * 2f) - 1f) * MathF.PI;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        return Vector3.Normalize(((1f - cos) * axis.Z * axis) + new Vector3(-sin * axis.Y, sin * axis.X, cos));
    }

    // Compressed texture coordinates outside 0..1 are scaled into it: stored = uv / scale + 0.5.
    internal static Vector2 DecompressUv(Vector2 stored, Vector2 scale)
    {
        return scale == Vector2.Zero ? stored : (stored - new Vector2(0.5f)) * scale;
    }

    // Each custom channel's size from its 3-bit format: RGBA8 unorm/snorm, RG half, RGBA half,
    // then R, RG, RGB and RGBA floats.
    private static int CustomSize(long format)
    {
        var size = 0;
        for (var channel = 0; channel < 4; channel++)
        {
            if ((format & (1L << (6 + channel))) == 0)
            {
                continue;
            }

            size += ((format >> (_customBase + (channel * _customBits))) & 7) switch
            {
                0 or 1 or 2 => 4,
                3 => 8,
                4 => 4,
                5 => 8,
                6 => 12,
                _ => 16,
            };
        }

        return size;
    }

    private static T? Get<T>(Dictionary<object, object?> dictionary, string key)
    {
        return dictionary.GetValueOrDefault(key) is T value ? value : default;
    }

    private static float Unorm16(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)) / 65535f;
    }

    private static float Float(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset));
    }
}
