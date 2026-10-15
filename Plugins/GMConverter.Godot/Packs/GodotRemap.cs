namespace GMConverter.Godot.Packs;

/// <summary>
/// Where an exported game keeps a source file: the <c>.import</c> file Godot's importer wrote (or
/// the <c>.remap</c> file for a scene or resource converted on export) points the source path at
/// the file the game loads instead.
/// </summary>
/// <param name="SourcePath">The path the project used, such as <c>res://audio/shot.ogg</c>.</param>
/// <param name="Importer">The importer that produced the file, such as <c>oggvorbisstr</c>; empty for an exported scene.</param>
/// <param name="Paths">The files the game loads, by variant (<c>path</c>, or <c>path.s3tc</c> and the like for textures).</param>
internal sealed record GodotRemap(string SourcePath, string Importer, IReadOnlyDictionary<string, string> Paths)
{
    public const string ImportExtension = ".import";
    public const string RemapExtension = ".remap";

    /// <summary>The file the game loads: the plain path, or the first variant.</summary>
    public string? TargetPath => Paths.GetValueOrDefault("path") ?? Paths.Values.FirstOrDefault();

    /// <summary>Whether a pack file is an <c>.import</c> or <c>.remap</c> file.</summary>
    public static bool IsRemapFile(string path)
    {
        return path.EndsWith(ImportExtension, StringComparison.Ordinal) || path.EndsWith(RemapExtension, StringComparison.Ordinal);
    }

    /// <summary>Reads the <c>[remap]</c> section of the <c>.import</c> or <c>.remap</c> file at <paramref name="path"/>.</summary>
    public static GodotRemap Parse(string path, string text)
    {
        var sourcePath = path[..path.LastIndexOf('.')];
        var importer = string.Empty;
        Dictionary<string, string> paths = new(StringComparer.Ordinal);
        var inRemap = false;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inRemap = line == "[remap]";
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (!inRemap || equals <= 0)
            {
                continue;
            }

            var key = line[..equals];
            var value = line[(equals + 1)..].Trim('"');
            if (key == "importer")
            {
                importer = value;
            }
            else if (key == "path" || key.StartsWith("path.", StringComparison.Ordinal))
            {
                paths[key] = value;
            }
        }

        return new GodotRemap(sourcePath, importer, paths);
    }
}
