using GMConverter.Godot.Packs;

namespace GMConverter.Godot.Tests;

public sealed class GodotRemapTests
{
    [Fact]
    public void ReadsImportFile()
    {
        const string text = "[remap]\n\nimporter=\"oggvorbisstr\"\ntype=\"AudioStreamOggVorbis\"\nuid=\"uid://j4ds3b3mvx13\"\npath=\"res://.godot/imported/shot.ogg-6341.oggvorbisstr\"\n\n[deps]\n\nsource_file=\"res://audio/shot.ogg\"\n";

        var remap = GodotRemap.Parse("res://audio/shot.ogg.import", text);

        Assert.Equal("res://audio/shot.ogg", remap.SourcePath);
        Assert.Equal("oggvorbisstr", remap.Importer);
        Assert.Equal("res://.godot/imported/shot.ogg-6341.oggvorbisstr", remap.TargetPath);
    }

    [Fact]
    public void ReadsTextureVariants()
    {
        const string text = "[remap]\n\nimporter=\"texture\"\npath.s3tc=\"res://.godot/imported/wood.png-1.s3tc.ctex\"\nmetadata={\n\"vram_texture\": true\n}\n";

        var remap = GodotRemap.Parse("res://models/wood.png.import", text);

        Assert.Equal("res://.godot/imported/wood.png-1.s3tc.ctex", remap.Paths["path.s3tc"]);
        Assert.Equal("res://.godot/imported/wood.png-1.s3tc.ctex", remap.TargetPath);
    }

    [Fact]
    public void ReadsExportedSceneRemap()
    {
        var remap = GodotRemap.Parse("res://instances/item.tscn.remap", "[remap]\n\npath=\"res://.godot/exported/1/export-2-item.scn\"\n");

        Assert.Equal("res://instances/item.tscn", remap.SourcePath);
        Assert.Equal(string.Empty, remap.Importer);
        Assert.Equal("res://.godot/exported/1/export-2-item.scn", remap.TargetPath);
    }
}
