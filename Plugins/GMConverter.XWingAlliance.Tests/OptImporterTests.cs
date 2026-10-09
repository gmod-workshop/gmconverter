using GMConverter.Plugins;
using GMConverter.SDK.Importers;
using GMConverter.XWingAlliance.Importers;
using JeremyAnsel.Xwa.Opt;

#pragma warning disable CS8602 // JeremyAnsel.Xwa.Opt exposes populated collections without nullable annotations.

namespace GMConverter.XWingAlliance.Tests;

public sealed class OptImporterTests
{
    [Fact]
    public void TexturePixelsAreConvertedFromBgraToRgba()
    {
        var path = WriteSingleQuadOpt(blue: 10, green: 20, red: 30);

        var model = new OPTImporter(new DefaultTextureFactory()).Parse(path, new ModelParseOptions(1f));

        var material = Assert.Single(model.Materials);
        Assert.Equal("colortexture", material.Name);
        Assert.Equal([30, 20, 10, 255], material.DiffuseTexture!.GetRgbaPixels()[..4]);
        Assert.Equal(2, Assert.Single(model.Meshes).Triangles.Count());
    }

    // One 32-bit 2x2 texture on a single quad face, which the importer splits into two triangles.
    private static string WriteSingleQuadOpt(byte blue, byte green, byte red)
    {
        var opt = new OptFile();
        var texture = new Texture
        {
            Name = "ColorTexture",
            Width = 2,
            Height = 2,
            ImageData = [.. Enumerable.Range(0, 4).SelectMany(_ => new[] { blue, green, red, (byte)255 })],
        };
        opt.Textures.Add(texture.Name, texture);

        var mesh = new Mesh(alloc: true);
        mesh.Vertices.Add(new Vector(0, 0, 0));
        mesh.Vertices.Add(new Vector(1, 0, 0));
        mesh.Vertices.Add(new Vector(1, 1, 0));
        mesh.Vertices.Add(new Vector(0, 1, 0));
        mesh.TextureCoordinates.Add(new TextureCoordinates(0, 0));
        mesh.TextureCoordinates.Add(new TextureCoordinates(1, 0));
        mesh.TextureCoordinates.Add(new TextureCoordinates(1, 1));
        mesh.TextureCoordinates.Add(new TextureCoordinates(0, 1));
        mesh.VertexNormals.Add(new Vector(0, 0, 1));

        var faceGroup = new FaceGroup(alloc: true);
        faceGroup.Textures.Add(texture.Name);
        faceGroup.Faces.Add(new Face
        {
            VerticesIndex = new Indices(0, 1, 2, 3),
            TextureCoordinatesIndex = new Indices(0, 1, 2, 3),
            VertexNormalsIndex = new Indices(0, 0, 0, 0),
        });
        var lod = new MeshLod();
        lod.FaceGroups.Add(faceGroup);
        mesh.Lods.Add(lod);
        opt.Meshes.Add(mesh);

        var directory = Path.Join(Path.GetTempPath(), "GMConverter.XWingAlliance.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Join(directory, "quad.opt");
        opt.Save(path);
        return path;
    }
}
