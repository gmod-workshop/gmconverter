using GMConverter.SDK.Importers;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
using GMConverter.SDK.Particles;

namespace GMConverter.UI.Tests;

public sealed class SchemaTestParticleImporter : IParticleImporter
{
    public string Format => "sampleparticle";
    public string DisplayName => "Test particle effect";
    public IReadOnlyList<string> FileExtensions { get; } = [".sampleparticle"];
    public OptionSchema OptionSchema { get; } = new([]);
    public object Summarize(string inputPath) => "Test particle effect";

    public ParticleEffect Import(ParticleImportContext context) => new(
        Path.GetFileNameWithoutExtension(context.InputPath),
        [new ParticleEmitter("sparks", new Material("spark", blendMode: MaterialBlendMode.Additive)) { SpawnRate = ParticleFloat.Constant(10f) }]);
}
