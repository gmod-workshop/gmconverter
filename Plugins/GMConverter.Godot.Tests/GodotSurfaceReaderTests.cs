using System.Buffers.Binary;
using System.Numerics;
using GMConverter.Godot.Geometry;
using GMConverter.Godot.Resources;
using static GMConverter.Godot.Tests.GodotResourceBuilder;

namespace GMConverter.Godot.Tests;

public sealed class GodotSurfaceReaderTests
{
    // Mesh format flags: vertex, normal, tangent, texture coordinates, index; version 2; compressed.
    private const long _vertex = 1;
    private const long _normal = 2;
    private const long _tangent = 4;
    private const long _uv = 16;
    private const long _index = 1 << 12;
    private const long _version2 = 1L << 35;
    private const long _compressed = 1 << 29;

    private static readonly Vector3[] _positions = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(0f, 2f, 0f)];
    private static readonly Vector2[] _uvs = [new(0f, 0f), new(1f, 0f), new(0f, 1f)];

    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, -1f, 0f)]
    [InlineData(0f, 0f, -1f)]
    [InlineData(0.48f, -0.6f, 0.64f)]
    public void OctahedronDecodeUndoesGodotsEncoding(float x, float y, float z)
    {
        var normal = Vector3.Normalize(new Vector3(x, y, z));
        var encoded = OctahedronEncode(normal);

        var decoded = GodotSurfaceReader.OctahedronDecode(encoded.X, encoded.Y);

        Assert.True(Vector3.Distance(normal, decoded) < 1e-4f, decoded.ToString());
    }

    [Fact]
    public void AxisAngleNormalIsTheRotationsThirdRow()
    {
        // 0.5 is no rotation; 1 is half a turn, here about X, which turns +Z to -Z.
        Assert.Equal(Vector3.UnitZ, GodotSurfaceReader.AxisAngleNormal(Vector3.UnitY, 0.5f));
        Assert.True(Vector3.Distance(-Vector3.UnitZ, GodotSurfaceReader.AxisAngleNormal(Vector3.UnitX, 1f)) < 1e-5f);
    }

    [Fact]
    public void ReadsSeparatedVertexBuffer()
    {
        // Godot 4.2: every position, then every normal and tangent.
        var vertexData = new List<byte>();
        foreach (var position in _positions)
        {
            vertexData.AddRange(Floats(position.X, position.Y, position.Z));
        }

        foreach (var _ in _positions)
        {
            vertexData.AddRange(Unorm16s(OctahedronEncode(Vector3.UnitZ)));
            vertexData.AddRange(Unorm16s(OctahedronEncode(Vector3.UnitX)));
        }

        var surface = ReadSurface(_vertex | _normal | _tangent | _uv | _index | _version2, [.. vertexData], [.. _uvs.SelectMany(uv => Floats(uv.X, uv.Y))]);

        Assert.Equal(_positions, surface.Positions);
        Assert.All(surface.Normals!, normal => Assert.True(Vector3.Distance(Vector3.UnitZ, normal) < 1e-4f));
        Assert.Equal(_uvs, surface.TextureCoordinates);
        Assert.Equal([0, 1, 2], surface.Indices);
    }

    [Fact]
    public void ReadsInterleavedVertexBufferFromBeforeGodot42()
    {
        var vertexData = new List<byte>();
        foreach (var position in _positions)
        {
            vertexData.AddRange(Floats(position.X, position.Y, position.Z));
            vertexData.AddRange(Unorm16s(OctahedronEncode(-Vector3.UnitY)));
            vertexData.AddRange(Unorm16s(OctahedronEncode(Vector3.UnitX)));
        }

        var surface = ReadSurface(_vertex | _normal | _tangent | _uv | _index, [.. vertexData], [.. _uvs.SelectMany(uv => Floats(uv.X, uv.Y))]);

        Assert.Equal(_positions, surface.Positions);
        Assert.All(surface.Normals!, normal => Assert.True(Vector3.Distance(-Vector3.UnitY, normal) < 1e-4f));
    }

    [Fact]
    public void ReadsCompressedSurface()
    {
        // Positions as fractions of the bounds, the frame's axis where the normal was and its
        // angle in the fourth component, and texture coordinates as 16-bit fractions.
        var aabbPosition = new Vector3(-1f, 0f, 0f);
        var aabbSize = new Vector3(2f, 2f, 1f);
        var vertexData = new List<byte>();
        foreach (var position in _positions)
        {
            var fraction = (position - aabbPosition) / aabbSize;
            vertexData.AddRange(Unorm16s(new Vector2(fraction.X, fraction.Y)));
            vertexData.AddRange(Unorm16s(new Vector2(fraction.Z, 0.5f)));
        }

        foreach (var _ in _positions)
        {
            vertexData.AddRange(Unorm16s(OctahedronEncode(Vector3.UnitY)));
        }

        var surface = ReadSurface(
            _vertex | _normal | _tangent | _uv | _index | _version2 | _compressed,
            [.. vertexData],
            [.. _uvs.SelectMany(uv => Unorm16s(uv))],
            Aabb(aabbPosition, aabbSize));

        Assert.All(surface.Positions.Zip(_positions), pair => Assert.True(Vector3.Distance(pair.First, pair.Second) < 1e-4f));
        Assert.All(surface.Normals!, normal => Assert.True(Vector3.Distance(Vector3.UnitZ, normal) < 1e-4f));
        Assert.All(surface.TextureCoordinates!.Zip(_uvs), pair => Assert.True(Vector2.Distance(pair.First, pair.Second) < 1e-4f));
    }

    [Fact]
    public void CompressedTextureCoordinatesScaleBack()
    {
        Assert.Equal(new Vector2(-2f, 4f), GodotSurfaceReader.DecompressUv(new Vector2(0f, 1f), new Vector2(4f, 8f)));
        Assert.Equal(new Vector2(0.25f, 0.75f), GodotSurfaceReader.DecompressUv(new Vector2(0.25f, 0.75f), Vector2.Zero));
    }

    /// <summary>Godot's <c>Vector3::octahedron_encode</c>.</summary>
    internal static Vector2 OctahedronEncode(Vector3 normal)
    {
        var n = normal / (MathF.Abs(normal.X) + MathF.Abs(normal.Y) + MathF.Abs(normal.Z));
        var o = n.Z >= 0f
            ? new Vector2(n.X, n.Y)
            : new Vector2((1f - MathF.Abs(n.Y)) * (n.X >= 0f ? 1f : -1f), (1f - MathF.Abs(n.X)) * (n.Y >= 0f ? 1f : -1f));
        return (o * 0.5f) + new Vector2(0.5f);
    }

    internal static byte[] Floats(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i]);
        }

        return bytes;
    }

    internal static byte[] Unorm16s(Vector2 value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)MathF.Round(Math.Clamp(value.X, 0f, 1f) * 65535f));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)MathF.Round(Math.Clamp(value.Y, 0f, 1f) * 65535f));
        return bytes;
    }

    private static GodotSurface ReadSurface(long format, byte[] vertexData, byte[] attributeData, Action<BinaryWriter>? aabb = null)
    {
        var builder = new GodotResourceBuilder();
        builder.Add("ArrayMesh_1", "ArrayMesh", ("_surfaces", Array(Surface(format, vertexData, attributeData, aabb))));
        var mesh = GodotResourceFile.Read(builder.Build("ArrayMesh")).Main;
        return Assert.Single(GodotSurfaceReader.Read(mesh));
    }

    /// <summary>A triangle surface dictionary as <c>ArrayMesh</c> saves it.</summary>
    internal static Action<BinaryWriter> Surface(long format, byte[] vertexData, byte[] attributeData, Action<BinaryWriter>? aabb = null, Action<BinaryWriter>? material = null)
    {
        var indices = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(indices, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(indices.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(indices.AsSpan(4), 2);
        List<(Action<BinaryWriter>, Action<BinaryWriter>)> pairs =
        [
            (Text("format"), Int64(format)),
            (Text("primitive"), Int(3)),
            (Text("vertex_data"), Bytes(vertexData)),
            (Text("vertex_count"), Int(3)),
            (Text("attribute_data"), Bytes(attributeData)),
            (Text("aabb"), aabb ?? Aabb(Vector3.Zero, new Vector3(1f, 2f, 1f))),
            (Text("index_data"), Bytes(indices)),
            (Text("index_count"), Int(3)),
            (Text("name"), Text("surface")),
        ];
        if (material is not null)
        {
            pairs.Add((Text("material"), material));
        }

        return Dictionary([.. pairs]);
    }
}
