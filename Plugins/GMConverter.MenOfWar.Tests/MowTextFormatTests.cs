using GMConverter.MenOfWar.Formats;

namespace GMConverter.MenOfWar.Tests;

public sealed class MowTextFormatTests
{
    [Fact]
    public void ParserReadsNestedNodesQuotedValuesAndSkipsComments()
    {
        var root = MOWTextParser.ParseFile(WriteFile("test.mdl", """
            {Skeleton ; bones follow
                {bone "turret base" {VolumeView "turret.ply"}}
            }
            """));

        var skeleton = Assert.Single(root.Children);
        Assert.Empty(skeleton.Values);
        var bone = Assert.Single(skeleton.Children);
        Assert.Equal("bone", bone.Name);
        Assert.Equal(["turret base"], bone.Values);
        Assert.Equal(["turret.ply"], bone.FirstChild("volumeview")!.Values);
    }

    [Fact]
    public void MaterialFileReadsTextureNamesWithoutPathsAndBlend()
    {
        var path = WriteFile("tank.mtl", """
            {material
                {diffuse "$/textures/units/tank.dds"}
                {bump "textures\units\tank_nm.dds"}
                {specular none}
                {blend test}
            }
            """);

        var material = MOWMaterialFile.Read(path);

        Assert.Equal("tank", material.Name);
        Assert.Equal("tank.dds", material.DiffuseTexture);
        Assert.Equal("tank_nm.dds", material.NormalTexture);
        Assert.Null(material.SpecularTexture);
        Assert.True(material.UsesAlpha);
    }

    [Fact]
    public void DefinitionResolvesExtensionModelRelativeToItself()
    {
        var path = WriteFile("tank.def", """{game_entity {Extension "models/tank.mdl"}}""");

        var modelPath = MOWDefinitionFile.Read(path).ResolveModelPath();

        Assert.Equal(Path.GetFullPath(Path.Join(Path.GetDirectoryName(path), "models", "tank.mdl")), modelPath);
    }

    private static string WriteFile(string name, string contents)
    {
        var directory = Path.Join(Path.GetTempPath(), "GMConverter.MenOfWar.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Join(directory, name);
        File.WriteAllText(path, contents);
        return path;
    }
}
