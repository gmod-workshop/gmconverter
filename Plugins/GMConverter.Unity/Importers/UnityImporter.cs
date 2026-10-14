using AssetsTools.NET.Extra;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;
using GMConverter.SDK.Textures;
using GMConverter.Unity.Animation;
using GMConverter.Unity.Assets;
using GMConverter.Unity.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GMConverter.Unity.Importers;

/// <summary>
/// Imports a model from a Unity player build through a <c>.unityasset</c> reference written by the
/// Unity explorer: a prefab or scene object with every renderer under it, or a single mesh.
/// </summary>
internal sealed class UnityImporter(ITextureFactory textureFactory, ILoggerFactory? loggerFactory = null) : IModelImporter
{
    private readonly ILogger _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<UnityImporter>();

    public const string IncludeInactive = "includeInactive";
    public const string ControllerAnimations = "controllerAnimations";
    public const string Animations = "animations";

    public string Format => "unity";

    public string DisplayName => "Unity";

    public IReadOnlyList<string> FileExtensions { get; } = [UnityAssetReference.FileExtension];

    public OptionSchema OptionSchema { get; } = new(
    [
        new OptionGroup("objects", "Objects",
        [
            new OptionDescriptor(IncludeInactive, OptionType.Bool, "Include hidden objects")
            {
                Description = "Also import renderers that are disabled or sit under inactive game objects, such as alternative outfits.",
                DefaultValue = false,
            },
        ]),
        new OptionGroup("animation", "Animation",
        [
            new OptionDescriptor(ControllerAnimations, OptionType.Bool, "Animator controller clips")
            {
                Description = "Import the clips the model's Animator controller plays.",
                DefaultValue = true,
            },
            new OptionDescriptor(Animations, OptionType.String, "Animation clips")
            {
                Description = "More clips to import, as the Explorer adds them (<file>:<path id>). Humanoid clips play on any humanoid character. Separate several with ';'.",
                AllowsMultiple = true,
            },
        ]),
    ]);

    public object Summarize(string inputPath)
    {
        var reference = UnityAssetReference.Read(inputPath);
        var library = UnityAssetLibrary.For(reference.DataDirectory);
        lock (library.SyncRoot)
        {
            var target = library.Get(reference.ObjectKey);
            return new UnityAssetSummary(target.Name, target.ClassId.ToString(), reference.Object);
        }
    }

    public Model Import(ModelImportContext context)
    {
        var reference = UnityAssetReference.Read(context.InputPath);
        var library = UnityAssetLibrary.For(reference.DataDirectory);
        var includeInactive = context.Options.GetBool(IncludeInactive);
        var animations = new UnityAnimationRequest(
            context.Options.GetBool(ControllerAnimations, defaultValue: true),
            [.. context.Options.GetStrings(Animations).Select(UnityObjectKey.Parse)]);

        lock (library.SyncRoot)
        {
            var target = library.Get(reference.ObjectKey);
            var builder = new UnityModelBuilder(library, textureFactory);
            var model = target.ClassId switch
            {
                AssetClassID.Transform => builder.BuildHierarchy(target, includeInactive, animations),
                AssetClassID.GameObject => builder.BuildHierarchy(FindTransform(library, target), includeInactive, animations),
                AssetClassID.Mesh => builder.BuildMesh(target, reference.MaterialRendererKey is { } renderer ? library.Find(renderer) : null),
                _ => throw new GMConverterException($"Unity {target.ClassId} objects cannot be imported as models."),
            };

            if (builder.SkippedTextures.Count > 0)
            {
                _logger.SkippedTextures(builder.SkippedTextures.Count, string.Join(", ", builder.SkippedTextures));
            }

            if (builder.SkippedAnimations.Count > 0)
            {
                _logger.SkippedAnimations(builder.SkippedAnimations.Count, string.Join(", ", builder.SkippedAnimations));
            }

            return Scale(model, context.ScaleFactor);
        }
    }

    private static UnityObject FindTransform(UnityAssetLibrary library, UnityObject gameObject)
    {
        return UnityFields.Items(gameObject["m_Component"])
            .Select(component => component["component"])
            .Where(pointer => library.PeekClass(gameObject.File, pointer) == AssetClassID.Transform)
            .Select(pointer => library.Resolve(gameObject.File, pointer))
            .OfType<UnityObject>()
            .FirstOrDefault()
            ?? throw new GMConverterException($"Unity game object {gameObject.Name} has no transform.");
    }

    private static Model Scale(Model model, float scale)
    {
        if (Math.Abs(scale - 1f) < 1e-6f)
        {
            return model;
        }

        return model with
        {
            Meshes = model.Meshes
                .Select(mesh => mesh with { Vertices = mesh.Vertices.Select(vertex => vertex with { Position = vertex.Position * scale }).ToArray() })
                .ToArray(),
            Skeleton = model.Skeleton is { } skeleton
                ? new Skeleton(skeleton.Bones
                    .Select(bone => bone with { LocalBindPose = bone.LocalBindPose with { Translation = bone.LocalBindPose.Translation * scale } })
                    .ToArray())
                : null,
            Animations = model.Animations?.Select(clip => Scale(clip, scale)).ToArray(),
        };
    }

    private static AnimationClip Scale(AnimationClip clip, float scale)
    {
        return clip with
        {
            Tracks = [.. clip.Tracks.Select(track => track is BoneTransformTrack bone
                ? bone with
                {
                    Keyframes = [.. bone.Keyframes.Select(key => key with { Transform = key.Transform with { Translation = key.Transform.Translation * scale } })],
                }
                : track)],
        };
    }
}
