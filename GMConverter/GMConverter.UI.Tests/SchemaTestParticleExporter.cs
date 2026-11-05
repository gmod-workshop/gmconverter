using System.Text.Json;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Options;
using GMConverter.SDK.Particles;

namespace GMConverter.UI.Tests;

public sealed class SchemaTestParticleExporter : IParticleExporter
{
    public string Format => "sampleparticle";
    public string DisplayName => "Test particle export";
    public OptionSchema OptionSchema { get; } = new([]);

    public ExportResult Export(ParticleEffect effect, ExportContext context)
    {
        var path = Path.Join(context.OutputDirectory, context.BaseName + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { effect.Name, Emitters = effect.Emitters.Select(emitter => emitter.Name) }));
        return new ExportResult([path]) { Warnings = [$"{effect.Emitters[0].Name}: test warning"] };
    }
}
