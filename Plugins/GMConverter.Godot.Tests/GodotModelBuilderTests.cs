using System.Numerics;
using GMConverter.Core.Textures;
using GMConverter.Godot.Geometry;
using GMConverter.Godot.Models;
using GMConverter.Godot.Packs;
using GMConverter.Godot.Scenes;
using GMConverter.SDK.Materials;
using static GMConverter.Godot.Tests.GodotResourceBuilder;

namespace GMConverter.Godot.Tests;

public sealed class GodotModelBuilderTests : IDisposable
{
    private const long _format = 1 | 2 | 16 | (1 << 12) | (1L << 35); // vertex, normal, uv, index; version 2

    private readonly string _directory = Path.Join(Path.GetTempPath(), "GMConverter.Godot.Tests", Guid.NewGuid().ToString("N"));

    public GodotModelBuilderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void InstancedSceneTakesTheInstancingNodesNameAndPlace()
    {
        var loader = Loader(
            ("res://prop.scn", Scene(["Prop", "Node3D", "Part"], [], [-1, -1, 1, 0, -1, 0, 0, 0, 0, 1, 2, -1, 0, 0])),
            ("res://room.scn", SceneWithInstance()));

        var root = GodotScene.Instantiate(loader, loader.Load("res://room.scn").Main);

        var prop = Assert.Single(root.Children);
        Assert.Equal("Chair", prop.Name);
        Assert.Equal("Node3D", prop.Type);
        Assert.Equal(new Vector3(5f, 0f, 0f), prop.World.Translation);
        Assert.Equal("Part", Assert.Single(prop.Children).Name);
    }

    [Fact]
    public void BuildsMeshInGodotSpaceWithReversedWinding()
    {
        // A triangle facing +Z (toward a glTF model's front) on a node moved up one meter, with a
        // red material.
        var vertexData = new List<byte>();
        Vector3[] positions = [new(0f, 0f, 0f), new(0f, 1f, 0f), new(1f, 0f, 0f)];
        foreach (var position in positions)
        {
            vertexData.AddRange(GodotSurfaceReaderTests.Floats(position.X, position.Y, position.Z));
        }

        foreach (var _ in positions)
        {
            vertexData.AddRange(GodotSurfaceReaderTests.Unorm16s(GodotSurfaceReaderTests.OctahedronEncode(Vector3.UnitZ)));
        }

        var uvs = GodotSurfaceReaderTests.Floats(0f, 0f, 0f, 1f, 1f, 0f);
        var builder = new GodotResourceBuilder();
        var material = builder.Add("Material_1", "StandardMaterial3D", ("resource_name", Text("red")), ("albedo_color", Color(1f, 0f, 0f, 1f)));
        var mesh = builder.Add("Mesh_1", "ArrayMesh", ("_surfaces", Array(GodotSurfaceReaderTests.Surface(_format, [.. vertexData], uvs, material: Internal(material)))));
        builder.Add("Scene_1", "PackedScene", ("_bundled", Bundle(
            ["Root", "Node3D", "Triangle", "MeshInstance3D", "transform", "mesh"],
            [Transform(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0f, 1f, 0f)), Internal(mesh)],
            [-1, -1, 1, 0, -1, 0, 0, 0, 0, 3, 2, -1, 2, 4, 0, 5, 1, 0])));
        var loader = Loader(("res://triangle.scn", builder.Build("PackedScene")));
        var root = GodotScene.Instantiate(loader, loader.Load("res://triangle.scn").Main);

        var model = new GodotModelBuilder(loader, new DefaultTextureFactory()).Build(root, "triangle", includeHidden: false);

        var sdkMesh = Assert.Single(model.Meshes);
        Assert.Equal(GodotSpace.Position(new Vector3(0f, 1f, 0f)), sdkMesh.Vertices[0].Position);
        Assert.Equal(new Vector3(0f, 0f, 2f), sdkMesh.Vertices[1].Position); // Godot up is SDK +Z
        Assert.True(Vector3.Distance(Vector3.UnitX, sdkMesh.Vertices[0].Normal) < 1e-4f); // Godot +Z, a model's front, is SDK +X
        Assert.Equal(new Vector2(0f, 1f), sdkMesh.Vertices[0].TextureCoordinate);
        var triangle = Assert.Single(sdkMesh.Triangles);
        Assert.True(Vector3.Dot(triangle.Normal(sdkMesh.Vertices), Vector3.UnitX) > 0.99f); // front faces turn counter-clockwise
        var sdkMaterial = Assert.Single(model.Materials);
        Assert.Equal("red", sdkMaterial.Name);
        Assert.Equal(MaterialBlendMode.Opaque, sdkMaterial.BlendMode);
        Assert.Equal([255, 0, 0, 255], sdkMaterial.DiffuseTexture!.GetRgbaPixels()[..4]);
        Assert.Null(model.Skeleton);
    }

    [Fact]
    public void SpaceConversionKeepsRotationsConsistentWithPositions()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1f, 2f, 3f)), 0.7f);
        var point = new Vector3(0.3f, -1.2f, 2f);

        var turnedThenConverted = GodotSpace.Position(Vector3.Transform(point, rotation));
        var convertedThenTurned = Vector3.Transform(GodotSpace.Position(point), GodotSpace.Rotation(rotation));

        Assert.True(Vector3.Distance(turnedThenConverted, convertedThenTurned) < 1e-5f);
    }

    [Fact]
    public void ParsesResourceUids()
    {
        // Godot's ResourceUID::id_to_text writes base 36, letters first.
        Assert.Equal(0, GodotRemap.ParseUid("uid://a"));
        Assert.Equal(26, GodotRemap.ParseUid("uid://0"));
        Assert.Equal(36 + 1, GodotRemap.ParseUid("uid://bb"));
        Assert.Equal(-1, GodotRemap.ParseUid("res://a.png"));
    }

    private GodotResourceLoader Loader(params (string Path, byte[] Data)[] files)
    {
        var path = Path.Join(_directory, "game.pck");
        File.WriteAllBytes(path, GodotPackTests.BuildPack(0, files));
        return new GodotResourceLoader(GodotPack.Open(path));
    }

    // A scene whose only child instances res://prop.scn as "Chair", five meters along X.
    private static byte[] SceneWithInstance()
    {
        var builder = new GodotResourceBuilder();
        var prop = builder.AddExternal("PackedScene", "res://prop.scn");
        builder.Add("Scene_1", "PackedScene", ("_bundled", Bundle(
            ["Room", "Node3D", "Chair", "transform"],
            [External(prop), Transform(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(5f, 0f, 0f))],
            [-1, -1, 1, 0, -1, 0, 0, 0, 0, 0x7FFFFFFF, 2, 0, 1, 3, 1, 0])));
        return builder.Build("PackedScene");
    }

    private static byte[] Scene(string[] names, Action<BinaryWriter>[] variants, int[] nodes)
    {
        var builder = new GodotResourceBuilder();
        builder.Add("Scene_1", "PackedScene", ("_bundled", Bundle(names, variants, nodes)));
        return builder.Build("PackedScene");
    }

    // A PackedScene's bundle. Each node is: parent, owner, type, name, instance, the property
    // count and (name, value) pairs, then the group count and groups.
    private static Action<BinaryWriter> Bundle(string[] names, Action<BinaryWriter>[] variants, int[] nodes)
    {
        var count = 0;
        for (var position = 0; position < nodes.Length; count++)
        {
            position += 5;
            position += 1 + (nodes[position] * 2);
            position += 1 + nodes[position];
        }

        return Dictionary(
            (Text("names"), Strings(names)),
            (Text("variants"), Array(variants)),
            (Text("node_count"), Int(count)),
            (Text("nodes"), Int32s(nodes)),
            (Text("node_paths"), Array()),
            (Text("version"), Int(3)));
    }
}
