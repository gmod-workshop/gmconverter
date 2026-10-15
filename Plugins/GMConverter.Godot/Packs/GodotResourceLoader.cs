using GMConverter.Godot.Resources;
using GMConverter.SDK.Common;

namespace GMConverter.Godot.Packs;

/// <summary>
/// Loads a game's resources by the paths its project used, the way the exported game does: a
/// file in the pack is read as is, and a source file (a <c>.glb</c>, a <c>.tscn</c>, a
/// <c>.png</c>) is read from where its <c>.import</c> or <c>.remap</c> file points. Resources are
/// cached for the loader's life.
/// </summary>
internal sealed class GodotResourceLoader(GodotPack pack)
{
    private static readonly string[] _imageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".tga", ".bmp", ".exr", ".hdr", ".svg"];

    private readonly Dictionary<string, GodotRemap> _remaps = pack.Remaps
        .GroupBy(remap => remap.SourcePath, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private readonly Dictionary<long, string> _pathsByUid = pack.Remaps
        .Where(remap => remap.Uid >= 0)
        .GroupBy(remap => remap.Uid)
        .ToDictionary(group => group.Key, group => group.First().SourcePath);

    private readonly Dictionary<string, GodotResourceFile> _files = new(StringComparer.Ordinal);

    public GodotPack Pack { get; } = pack;

    /// <summary>The pack file a project path loads from, or null when the game lacks it.</summary>
    public string? Resolve(string path)
    {
        if (_remaps.TryGetValue(path, out var remap))
        {
            // Textures name one file per compression; desktop games load the S3TC one.
            var target = remap.Paths.GetValueOrDefault("path.s3tc") ?? remap.Paths.GetValueOrDefault("path.bptc") ?? remap.TargetPath;
            return target is not null && Pack.Find(target) is not null ? target : null;
        }

        return Pack.Find(path) is not null ? path : null;
    }

    /// <summary>The bytes of the file a project path loads from.</summary>
    public byte[] ReadBytes(string path)
    {
        return Pack.Read(Resolve(path) ?? throw new GMConverterException($"{path} is not in the Godot game."));
    }

    /// <summary>The binary resource a project path loads.</summary>
    public GodotResourceFile Load(string path)
    {
        var target = Resolve(path) ?? throw new GMConverterException($"{path} is not in the Godot game.");
        if (!_files.TryGetValue(target, out var file))
        {
            var data = Pack.Read(target);
            if (!GodotResourceFile.IsResource(data))
            {
                throw new GMConverterException($"{path} is a text resource, which is not supported.");
            }

            file = GodotResourceFile.Read(data);
            _files[target] = file;
        }

        return file;
    }

    /// <summary>A resource another refers to: one in the same file, or the main resource of another file.</summary>
    public GodotResource? Get(object? reference)
    {
        return reference switch
        {
            GodotResource resource => resource,
            GodotExternalResource external when !IsTexture(external) && PathOf(external) is { } path => Load(path).Main,
            _ => null,
        };
    }

    /// <summary>
    /// The project path a reference to another file loads: its path, or, when no file has that path
    /// any more, the path of the file with its unique id, as Godot falls back to. A missing image
    /// also falls back to an image of the same name in another format, since projects often
    /// re-save textures (a <c>.jpg</c> as a <c>.png</c>) after a model's import refers to them.
    /// </summary>
    public string? PathOf(GodotExternalResource external)
    {
        if (Resolve(external.Path) is not null)
        {
            return external.Path;
        }

        if (_pathsByUid.TryGetValue(external.Uid, out var path) && Resolve(path) is not null)
        {
            return path;
        }

        if (!IsTexture(external))
        {
            return null;
        }

        var stem = external.Path[..^Path.GetExtension(external.Path).Length];
        return _imageExtensions.Select(extension => stem + extension).FirstOrDefault(candidate => Resolve(candidate) is not null);
    }

    private static bool IsTexture(GodotExternalResource external)
    {
        return external.Type.Contains("Texture", StringComparison.Ordinal);
    }
}
