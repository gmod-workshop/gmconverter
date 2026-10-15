using System.Numerics;
using GMConverter.Godot.Resources;
using ZstdSharp;
using static GMConverter.Godot.Tests.GodotResourceBuilder;

namespace GMConverter.Godot.Tests;

public sealed class GodotResourceFileTests
{
    [Fact]
    public void ReadsPropertiesAndReferences()
    {
        var builder = new GodotResourceBuilder();
        var texture = builder.AddExternal("Texture2D", "res://wood.png");
        var material = builder.Add("Material_1", "StandardMaterial3D", ("albedo_texture", External(texture)), ("roughness", Float(0.5f)));
        builder.Add("Scene_1", "Holder",
            ("material", Internal(material)),
            ("name", Text("table")),
            ("count", Int(-3)),
            ("visible", Bool(true)),
            ("offset", Vector(new Vector3(1f, 2f, 3f))),
            ("place", Transform(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(4f, 5f, 6f))),
            ("data", Bytes([9, 8, 7, 6, 5])),
            ("after", Int(42)),
            ("track", NodePath(["Skeleton3D"], ["Hips"])),
            ("list", Array(Int(1), Text("two"))),
            ("map", Dictionary((Text("key"), Int(7)))));

        var file = GodotResourceFile.Read(builder.Build("Holder"));

        Assert.Equal("Holder", file.Type);
        Assert.Equal(new GodotExternalResource("Texture2D", "res://wood.png"), Assert.Single(file.ExternalResources));
        var main = file.Main;
        var referenced = Assert.IsType<GodotResource>(main["material"]);
        Assert.Same(file.Resources[0], referenced);
        Assert.Equal("StandardMaterial3D", referenced.Type);
        Assert.Equal(0.5, referenced["roughness"]);
        Assert.Equal(new GodotExternalResource("Texture2D", "res://wood.png"), referenced["albedo_texture"]);
        Assert.Equal("table", main["name"]);
        Assert.Equal(-3L, main["count"]);
        Assert.Equal(true, main["visible"]);
        Assert.Equal(new Vector3(1f, 2f, 3f), main["offset"]);
        Assert.Equal(new Vector3(4f, 5f, 6f), Assert.IsType<GodotTransform3D>(main["place"]).Origin);
        Assert.Equal<byte>([9, 8, 7, 6, 5], Assert.IsType<byte[]>(main["data"]));
        Assert.Equal(42L, main["after"]); // read past the byte array's padding
        Assert.Equal("Skeleton3D:Hips", main["track"]?.ToString());
        Assert.Equal([1L, "two"], Assert.IsType<List<object?>>(main["list"]));
        Assert.Equal(7L, Assert.IsType<Dictionary<object, object?>>(main["map"])["key"]);
    }

    [Fact]
    public void ReadsCompressedResource()
    {
        var builder = new GodotResourceBuilder();
        builder.Add("Res_1", "Resource", ("name", Text("compressed")));
        // Godot compresses everything after the magic, in blocks.
        var contents = builder.Build("Resource", withoutMagic: true);
        const int blockSize = 16;
        var blocks = contents.Chunk(blockSize).Select(block => new Compressor().Wrap(block).ToArray()).ToArray();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write("RSCC"u8);
            writer.Write(2); // zstd
            writer.Write(blockSize);
            writer.Write(contents.Length);
            var blockCount = (contents.Length / blockSize) + 1;
            for (var index = 0; index < blockCount; index++)
            {
                writer.Write(index < blocks.Length ? blocks[index].Length : 0);
            }

            foreach (var block in blocks)
            {
                writer.Write(block);
            }
        }

        var file = GodotResourceFile.Read(stream.ToArray());

        Assert.Equal("compressed", file.Main["name"]);
    }

    [Fact]
    public void TransformMatrixMovesPointsLikeGodot()
    {
        // A quarter turn about Y (Godot's basis columns are the turned axes) and a move along X.
        var transform = new GodotTransform3D(new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(10f, 0f, 0f));

        var moved = Vector3.Transform(Vector3.UnitX, transform.ToMatrix());

        Assert.Equal(10f, moved.X, 5);
        Assert.Equal(0f, moved.Y, 5);
        Assert.Equal(-1f, moved.Z, 5);
    }
}
