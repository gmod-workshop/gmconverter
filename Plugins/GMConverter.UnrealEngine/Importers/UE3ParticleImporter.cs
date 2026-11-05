using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
using GMConverter.SDK.Particles;
using GMConverter.SDK.Textures;
using GMConverter.UnrealEngine.Formats.UE3.Particles;

namespace GMConverter.UnrealEngine.Importers;

/// <summary>Reads a Cascade particle system the explorer exported (<see cref="UE3ParticleSidecar"/>) into a <see cref="ParticleEffect"/>.</summary>
internal sealed class UE3ParticleImporter(ITextureFactory textureFactory) : IParticleImporter
{
    public const string FormatId = "ue3fx";

    public string Format => FormatId;

    public string DisplayName => "Unreal Engine 3 particle system (Unreal Explorer)";

    public IReadOnlyList<string> FileExtensions { get; } = [UE3ParticleSidecar.Extension];

    public OptionSchema OptionSchema => OptionSchema.Empty;

    public ParticleEffect Import(ParticleImportContext context)
    {
        var sidecar = UE3ParticleSidecar.Read(context.InputPath);
        var directory = Path.GetDirectoryName(context.InputPath)!;
        Dictionary<string, Texture> textures = new(StringComparer.OrdinalIgnoreCase);
        Texture? Load(string? file)
        {
            if (file is null || !File.Exists(Path.Join(directory, file)))
            {
                return null;
            }

            if (!textures.TryGetValue(file, out var texture))
            {
                texture = textureFactory.FromFile(Path.Join(directory, file), hasAlpha: true, name: Path.GetFileNameWithoutExtension(file));
                textures[file] = texture;
            }

            return texture;
        }

        return new ParticleEffect(sidecar.Name, [.. sidecar.Emitters.Select(emitter => emitter.Settings with
        {
            Material = new Material(
                emitter.Settings.Name,
                diffuseTexture: Load(emitter.Texture),
                blendMode: emitter.Blend,
                uvTransform: emitter.UvScale is { } scale ? new MaterialUvTransform { Scale = scale } : null),
            Mesh = emitter.Mesh is { } mesh ? LoadMesh(Path.Join(directory, mesh)) : null,
        })]);
    }

    private Model LoadMesh(string path) =>
        new PSKImporter(textureFactory).Import(new ModelImportContext(path)) with { Name = Path.GetFileNameWithoutExtension(path) };

    public object Summarize(string inputPath)
    {
        var sidecar = UE3ParticleSidecar.Read(inputPath);
        return new
        {
            sidecar.Name,
            Emitters = sidecar.Emitters.Select(emitter => emitter.Settings.Name).ToArray(),
        };
    }
}
