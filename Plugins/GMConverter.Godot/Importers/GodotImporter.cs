using GMConverter.Godot.Models;
using GMConverter.Godot.Packs;
using GMConverter.Godot.Scenes;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;
using GMConverter.SDK.Textures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GMConverter.Godot.Importers;

/// <summary>
/// Imports a model from a Godot game through a <c>.godotscene</c> reference written by the Godot
/// explorer: a scene (an imported model or one of the game's own scenes) with every mesh in it
/// and the animations its animation players play on them.
/// </summary>
internal sealed class GodotImporter(ITextureFactory textureFactory, ILoggerFactory? loggerFactory = null) : IModelImporter
{
    public const string IncludeHidden = "includeHidden";
    public const string Animations = "animations";

    private readonly ILogger _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<GodotImporter>();

    public string Format => "godot";

    public string DisplayName => "Godot";

    public IReadOnlyList<string> FileExtensions { get; } = [GodotAssetReference.FileExtension];

    public OptionSchema OptionSchema { get; } = new(
    [
        new OptionGroup("objects", "Objects",
        [
            new OptionDescriptor(IncludeHidden, OptionType.Bool, "Include hidden nodes")
            {
                Description = "Also import meshes the scene hides, such as items shown only while they are used.",
                DefaultValue = false,
            },
        ]),
        new OptionGroup("animation", "Animation",
        [
            new OptionDescriptor(Animations, OptionType.Bool, "Import animations")
            {
                Description = "Import the animations the scene's animation players play on the model.",
                DefaultValue = true,
            },
        ]),
    ]);

    public object Summarize(string inputPath)
    {
        var reference = GodotAssetReference.Read(inputPath);
        return new { Name = Path.GetFileNameWithoutExtension(reference.Scene), reference.Scene, reference.Pack };
    }

    public Model Import(ModelImportContext context)
    {
        var reference = GodotAssetReference.Read(context.InputPath);
        var loader = new GodotResourceLoader(GodotPack.For(reference.Pack));
        var scene = loader.Load(reference.Scene).Main;
        if (scene.Type != "PackedScene")
        {
            throw new GMConverterException($"{reference.Scene} is a Godot {scene.Type}, not a scene.");
        }

        var root = GodotScene.Instantiate(loader, scene);
        var builder = new GodotModelBuilder(loader, textureFactory);
        var model = builder.Build(
            root,
            Path.GetFileNameWithoutExtension(reference.Scene),
            context.Options.GetBool(IncludeHidden),
            context.Options.GetBool(Animations, defaultValue: true));

        if (builder.SkippedTextures.Count > 0)
        {
            _logger.SkippedTextures(builder.SkippedTextures.Count, string.Join(", ", builder.SkippedTextures));
        }

        if (builder.SkippedMeshes.Count > 0)
        {
            _logger.SkippedMeshes(builder.SkippedMeshes.Count, string.Join(", ", builder.SkippedMeshes));
        }

        return Scale(model, context.ScaleFactor);
    }

    private static Model Scale(Model model, float scale)
    {
        if (Math.Abs(scale - 1f) < 1e-6f)
        {
            return model;
        }

        return model with
        {
            Meshes = [.. model.Meshes.Select(mesh => mesh with
            {
                Vertices = [.. mesh.Vertices.Select(vertex => vertex with { Position = vertex.Position * scale })],
            })],
            Skeleton = model.Skeleton is { } skeleton
                ? new Skeleton([.. skeleton.Bones.Select(bone => bone with { LocalBindPose = bone.LocalBindPose with { Translation = bone.LocalBindPose.Translation * scale } })])
                : null,
            Animations = model.Animations?.Select(clip => clip with
            {
                Tracks = [.. clip.Tracks.Select(track => track is BoneTransformTrack bone
                    ? bone with { Keyframes = [.. bone.Keyframes.Select(key => key with { Transform = key.Transform with { Translation = key.Transform.Translation * scale } })] }
                    : track)],
            }).ToArray(),
        };
    }
}
