using System.Buffers.Binary;
using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.Unity.Assets;

namespace GMConverter.Unity.Geometry;

/// <summary>
/// Decodes a <c>Mesh</c> object (Unity 2019 and later vertex layout): vertex channels from the
/// interleaved streams, triangle lists per submesh and the skinning bind poses.
/// </summary>
internal static class UnityMeshReader
{
    private const int _positionChannel = 0;
    private const int _normalChannel = 1;
    private const int _uv0Channel = 4;
    private const int _blendWeightChannel = 12;
    private const int _blendIndicesChannel = 13;

    private const int _trianglesTopology = 0;
    private const int _quadsTopology = 2;

    public static UnityMeshData Read(UnityAssetLibrary library, UnityObject mesh)
    {
        var vertexData = mesh["m_VertexData"];
        var vertexCount = (int)vertexData["m_VertexCount"].AsUInt;
        var channels = vertexData["m_Channels"]["Array"].Children
            .Select(channel => new Channel(
                channel["stream"].AsByte,
                channel["offset"].AsByte,
                (UnityVertexFormat)channel["format"].AsByte,
                channel["dimension"].AsByte & 0xF))
            .ToArray();
        var data = library.ReadStreamedData(mesh["m_StreamData"], vertexData["m_DataSize"].AsByteArray);
        var streams = GetStreams(channels, vertexCount);

        var positions = ReadVectors3(data, channels, streams, _positionChannel, vertexCount)
            ?? throw new GMConverterException($"Unity mesh {mesh.Name} has no vertex positions.");
        var normals = ReadVectors3(data, channels, streams, _normalChannel, vertexCount);
        var uv0 = ReadChannel(data, channels, streams, _uv0Channel, vertexCount) is { } uvs
            ? Enumerable.Range(0, vertexCount).Select(i => new Vector2(uvs.Values[i * uvs.Dimension], uvs.Dimension > 1 ? uvs.Values[i * uvs.Dimension + 1] : 0f)).ToArray()
            : null;
        var (boneIndices, boneWeights) = ReadInfluences(data, channels, streams, vertexCount);

        return new UnityMeshData(
            mesh.Name,
            positions,
            normals,
            uv0,
            boneIndices,
            boneWeights,
            ReadSubmeshes(mesh, vertexCount),
            [.. mesh["m_BindPose"]["Array"].Children.Select(UnitySpace.ReadMatrix)]);
    }

    private static List<int[]> ReadSubmeshes(UnityObject mesh, int vertexCount)
    {
        var indexBuffer = UnityFields.ReadBytes(mesh["m_IndexBuffer"]);
        var is32Bit = mesh["m_IndexFormat"].AsInt == 1;
        var indexSize = is32Bit ? 4 : 2;
        List<int[]> submeshes = [];

        foreach (var submesh in mesh["m_SubMeshes"]["Array"].Children)
        {
            var firstIndex = (int)(submesh["firstByte"].AsUInt / indexSize);
            var indexCount = (int)submesh["indexCount"].AsUInt;
            var topology = submesh["topology"].AsInt;
            var baseVertex = (int)submesh["baseVertex"].AsUInt;

            var indices = new int[indexCount];
            for (var i = 0; i < indexCount; i++)
            {
                var offset = (firstIndex + i) * indexSize;
                var index = is32Bit
                    ? (int)BinaryPrimitives.ReadUInt32LittleEndian(indexBuffer.AsSpan(offset))
                    : BinaryPrimitives.ReadUInt16LittleEndian(indexBuffer.AsSpan(offset));
                indices[i] = Math.Clamp(index + baseVertex, 0, Math.Max(0, vertexCount - 1));
            }

            submeshes.Add(topology switch
            {
                _trianglesTopology => indices,
                _quadsTopology => QuadsToTriangles(indices),
                _ => [],
            });
        }

        return submeshes;
    }

    private static int[] QuadsToTriangles(int[] quads)
    {
        var triangles = new int[quads.Length / 4 * 6];
        for (int quad = 0, output = 0; quad + 3 < quads.Length; quad += 4)
        {
            triangles[output++] = quads[quad];
            triangles[output++] = quads[quad + 1];
            triangles[output++] = quads[quad + 2];
            triangles[output++] = quads[quad];
            triangles[output++] = quads[quad + 2];
            triangles[output++] = quads[quad + 3];
        }

        return triangles;
    }

    private static (int[]? Indices, float[]? Weights) ReadInfluences(byte[] data, Channel[] channels, VertexStream[] streams, int vertexCount)
    {
        if (ReadChannel(data, channels, streams, _blendIndicesChannel, vertexCount) is not { } indices)
        {
            return (null, null);
        }

        var weights = ReadChannel(data, channels, streams, _blendWeightChannel, vertexCount);
        var boneIndices = new int[vertexCount * UnityMeshData.InfluencesPerVertex];
        var boneWeights = new float[vertexCount * UnityMeshData.InfluencesPerVertex];

        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            var influenceCount = Math.Min(indices.Dimension, UnityMeshData.InfluencesPerVertex);
            var assigned = 0f;
            for (var slot = 0; slot < influenceCount; slot++)
            {
                var output = vertex * UnityMeshData.InfluencesPerVertex + slot;
                boneIndices[output] = (int)indices.Values[vertex * indices.Dimension + slot];

                // Unity drops the last weight when it can be derived: the weights sum to one.
                var weight = weights is not null && slot < weights.Dimension
                    ? weights.Values[vertex * weights.Dimension + slot]
                    : slot == (weights?.Dimension ?? 0) ? 1f - assigned : 0f;
                boneWeights[output] = Math.Max(0f, weight);
                assigned += boneWeights[output];
            }
        }

        return (boneIndices, boneWeights);
    }

    private static Vector3[]? ReadVectors3(byte[] data, Channel[] channels, VertexStream[] streams, int channelIndex, int vertexCount)
    {
        if (ReadChannel(data, channels, streams, channelIndex, vertexCount) is not { Dimension: >= 3 } values)
        {
            return null;
        }

        var vectors = new Vector3[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            var offset = i * values.Dimension;
            vectors[i] = new Vector3(values.Values[offset], values.Values[offset + 1], values.Values[offset + 2]);
        }

        return vectors;
    }

    private static ChannelValues? ReadChannel(byte[] data, Channel[] channels, VertexStream[] streams, int channelIndex, int vertexCount)
    {
        if (channelIndex >= channels.Length || channels[channelIndex].Dimension == 0)
        {
            return null;
        }

        var channel = channels[channelIndex];
        var stream = streams[channel.Stream];
        var componentSize = ComponentSize(channel.Format);
        var values = new float[vertexCount * channel.Dimension];

        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            var vertexOffset = stream.Offset + vertex * stream.Stride + channel.Offset;
            for (var component = 0; component < channel.Dimension; component++)
            {
                var offset = vertexOffset + component * componentSize;
                if (offset + componentSize > data.Length)
                {
                    throw new GMConverterException("Unity mesh vertex data is shorter than its channels describe.");
                }

                values[vertex * channel.Dimension + component] = ReadComponent(data.AsSpan(offset), channel.Format);
            }
        }

        return new ChannelValues(values, channel.Dimension);
    }

    private static float ReadComponent(ReadOnlySpan<byte> bytes, UnityVertexFormat format)
    {
        return format switch
        {
            UnityVertexFormat.Float => BinaryPrimitives.ReadSingleLittleEndian(bytes),
            UnityVertexFormat.Float16 => (float)BinaryPrimitives.ReadHalfLittleEndian(bytes),
            UnityVertexFormat.UNorm8 => bytes[0] / 255f,
            UnityVertexFormat.SNorm8 => Math.Max((sbyte)bytes[0] / 127f, -1f),
            UnityVertexFormat.UNorm16 => BinaryPrimitives.ReadUInt16LittleEndian(bytes) / 65535f,
            UnityVertexFormat.SNorm16 => Math.Max(BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32767f, -1f),
            UnityVertexFormat.UInt8 => bytes[0],
            UnityVertexFormat.SInt8 => (sbyte)bytes[0],
            UnityVertexFormat.UInt16 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
            UnityVertexFormat.SInt16 => BinaryPrimitives.ReadInt16LittleEndian(bytes),
            UnityVertexFormat.UInt32 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            UnityVertexFormat.SInt32 => BinaryPrimitives.ReadInt32LittleEndian(bytes),
            _ => throw new GMConverterException($"Unsupported Unity vertex format {format}."),
        };
    }

    private static int ComponentSize(UnityVertexFormat format)
    {
        return format switch
        {
            UnityVertexFormat.Float or UnityVertexFormat.UInt32 or UnityVertexFormat.SInt32 => 4,
            UnityVertexFormat.Float16 or UnityVertexFormat.UNorm16 or UnityVertexFormat.SNorm16 or
                UnityVertexFormat.UInt16 or UnityVertexFormat.SInt16 => 2,
            _ => 1,
        };
    }

    // Streams sit one after another, each padded to 16 bytes; a stream's stride is the end of its
    // furthest channel.
    private static VertexStream[] GetStreams(Channel[] channels, int vertexCount)
    {
        var streamCount = channels.Length == 0 ? 0 : channels.Max(channel => channel.Stream) + 1;
        var streams = new VertexStream[streamCount];
        var offset = 0;

        for (var index = 0; index < streamCount; index++)
        {
            var stride = channels
                .Where(channel => channel.Stream == index && channel.Dimension > 0)
                .Select(channel => channel.Offset + ComponentSize(channel.Format) * channel.Dimension)
                .DefaultIfEmpty(0)
                .Max();
            streams[index] = new VertexStream(offset, stride);
            offset += stride * vertexCount;
            offset = (offset + 15) & ~15;
        }

        return streams;
    }

    private readonly record struct Channel(int Stream, int Offset, UnityVertexFormat Format, int Dimension);

    private readonly record struct VertexStream(int Offset, int Stride);

    private sealed record ChannelValues(float[] Values, int Dimension);
}
