using System.Numerics;
using GMConverter.Core.Textures;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
using GMConverter.SDK.Particles;
using GMConverter.SourceEngine.Exporters;

namespace GMConverter.SourceEngine.Tests;

public sealed class PCFExporterTests
{
    [Fact]
    public void EmittersBecomeChildSystemsOfTheEffect()
    {
        var (output, result) = Export(Glow(), "garrysmod");

        var (header, root) = DmxTestReader.Read(Path.Join(output, "particles", "fx.pcf"));
        Assert.Equal("<!-- dmx encoding binary 2 format pcf 1 -->\n", header);
        var systems = root.Children("particleSystemDefinitions");
        Assert.Equal(["P_Glow", "P_Glow_core", "P_Glow_sparks"], systems.Select(system => system.Name));
        Assert.Equal(["P_Glow_core", "P_Glow_sparks"], systems[0].Children("children").Select(child => child.Get<DmxTestReader.Element>("child").Name));
        Assert.Contains(Path.Join(output, "lua", "autorun", "fx_particles.lua"), result.Files);
    }

    [Fact]
    public void EmitterPropertiesMapToSourceOperatorsInInches()
    {
        var (output, _) = Export(Glow(), "garrysmod");
        var core = DmxTestReader.Read(Path.Join(output, "particles", "fx.pcf")).Root.Children("particleSystemDefinitions")[1];

        Assert.Equal("particle/gmconverter/fx/p_glow_core.vmt", core.Get<string>("material"));
        var burst = core.Function("emitters", "emit_instantaneously");
        Assert.Equal(0.25f, burst.Get<float>("emission_start_time"), 4);
        Assert.Equal(3, burst.Get<int>("num_to_emit"));
        var lifetime = core.Function("initializers", "Lifetime Random");
        Assert.Equal((0.5f, 1f), (lifetime.Get<float>("lifetime_min"), lifetime.Get<float>("lifetime_max")));
        // 10 cm wide sprites have a radius of about two inches.
        Assert.Equal(1.9685f, core.Function("initializers", "Radius Random").Get<float>("radius_max"), 3);
        Assert.Equal(new Vector3(0, 0, -386.22f), core.Function("operators", "Movement Basic").Get<Vector3>("gravity"), new Vector3Comparer(0.1f));
        Assert.Contains("Movement Lock to Control Point", core.Functions("operators"));
        // Size over life 1 -> 2 -> 0 chains two Radius Scales.
        Assert.Equal([(1f, 2f), (2f, 0f)], core.Children("operators").Where(op => op.Get<string>("functionName") == "Radius Scale")
            .Select(op => (op.Get<float>("radius_start_scale"), op.Get<float>("radius_end_scale"))));
        Assert.True(core.Function("renderers", "render_animated_sprites").Get<bool>("animation_fit_lifetime"));
    }

    [Fact]
    public void FlipbooksBecomeSpriteSheetsBeforeTheImage()
    {
        var (output, _) = Export(Glow(), "garrysmod");
        var vtf = File.ReadAllBytes(Path.Join(output, "materials", "particle", "gmconverter", "fx", "p_glow_core.vtf"));

        Assert.Equal(2, BitConverter.ToInt32(vtf, 68));
        Assert.Equal(0x10, BitConverter.ToInt32(vtf, 80));
        Assert.Equal(0x30, BitConverter.ToInt32(vtf, 88));
        // One sequence over the 2x2 grid's four frames.
        var sheet = BitConverter.ToInt32(vtf, 84);
        Assert.Equal(1, BitConverter.ToInt32(vtf, sheet + 8));
        Assert.Equal(4, BitConverter.ToInt32(vtf, sheet + 20));
        var vmt = File.ReadAllText(Path.Join(output, "materials", "particle", "gmconverter", "fx", "p_glow_core.vmt"));
        Assert.Contains("\"$additive\" \"1\"", vmt);
    }

    [Fact]
    public void DeathSpawnsFollowTheirSourcesScheduleOneLifetimeLater()
    {
        var (output, result) = Export(Glow(), "garrysmod");
        var sparks = DmxTestReader.Read(Path.Join(output, "particles", "fx.pcf")).Root.Children("particleSystemDefinitions")[2];

        // The core bursts 3 at 0.25 s and lives 0.5-1 s; each death releases 2 sparks.
        var burst = sparks.Function("emitters", "emit_instantaneously");
        Assert.Equal(0.25f + 0.75f, burst.Get<float>("emission_start_time"), 4);
        Assert.Equal(6, burst.Get<int>("num_to_emit"));
        Assert.Contains(result.Warnings, warning => warning.StartsWith("P_Glow_sparks: spawns where core's particles die", StringComparison.Ordinal));
    }

    [Fact]
    public void BrighterThanWhiteKeepsItsHue()
    {
        var effect = Glow();
        var bright = effect with { Emitters = [effect.Emitters[0] with { StartColor = ParticleVector.Constant(new Vector3(25f, 10f, 2f)) }] };
        var (output, _) = Export(bright, "garrysmod");

        var color = DmxTestReader.Read(Path.Join(output, "particles", "fx.pcf")).Root.Children("particleSystemDefinitions")[0]
            .Function("initializers", "Color Random").Get<byte[]>("color1");
        // The colour keeps its hue, (1, 0.4, 0.08) in sRGB, and the 25x goes into the texture,
        // whose 200 (0.58 linear) saturates.
        Assert.Equal([255, 170, 80, 255], color);
        var vtf = File.ReadAllBytes(Path.Join(output, "materials", "particle", "gmconverter", "fx", "p_glow_core.vtf"));
        Assert.Equal(255, vtf[^4]);
    }

    [Fact]
    public void ViewModelEffectsAreFlaggedToDrawWithTheViewModel()
    {
        var output = Path.Join(Path.GetTempPath(), "GMConverter.PCFTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var options = new OptionValues(new Dictionary<string, object?> { [PCFExporter.ViewModelOption] = true });
        new PCFExporter().Export(Glow(), new ExportContext(output, "fx", options) { TargetGameId = "garrysmod" });

        var systems = DmxTestReader.Read(Path.Join(output, "particles", "fx.pcf")).Root.Children("particleSystemDefinitions");
        Assert.All(systems, system => Assert.True(system.Get<bool>("view model effect")));
        Assert.False(DmxTestReader.Read(Path.Join(Export(Glow(), "garrysmod").Output, "particles", "fx.pcf")).Root
            .Children("particleSystemDefinitions")[0].Get<bool>("view model effect"));
    }

    [Fact]
    public void WhatSourceCantDoIsReported()
    {
        var (_, result) = Export(Glow(), "source");

        Assert.Contains(result.Warnings, warning => warning.Contains("particles_manifest.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void TextureSlidesOverLifeAreReportedAsLeftOut()
    {
        var effect = Glow();
        var sliding = effect with
        {
            Emitters = [effect.Emitters[0] with { UvOffsetOverLife = ParticleVector.Curve([new ParticleVectorKey(0f, Vector3.Zero), new ParticleVectorKey(1f, Vector3.UnitX)]) }],
        };
        var (_, result) = Export(sliding, "garrysmod");

        Assert.Contains(result.Warnings, warning => warning.StartsWith("P_Glow: texture slide over life is not written", StringComparison.Ordinal));
    }

    [Fact]
    public void GarrysModAddsTheFileAndPrecachesItsSystems()
    {
        var (output, _) = Export(Glow(), "garrysmod");

        var lua = File.ReadAllText(Path.Join(output, "lua", "autorun", "fx_particles.lua"));
        Assert.Contains("game.AddParticles(\"particles/fx.pcf\")", lua);
        Assert.Contains("\tPrecacheParticleSystem(\"P_Glow\")", lua);
    }

    private static ParticleEffect Glow()
    {
        var texture = new DefaultTextureFactory().FromRgba("glow", 8, 8, Enumerable.Repeat((byte)200, 8 * 8 * 4).ToArray(), hasAlpha: true);
        var core = new ParticleEmitter("core", new Material("glow", diffuseTexture: texture, blendMode: MaterialBlendMode.Additive))
        {
            DelaySeconds = 0.25f,
            Bursts = [new ParticleBurst(0f, 3, 3)],
            Lifetime = ParticleFloat.Range(0.5f, 1f),
            StartSize = ParticleVector.Constant(new Vector3(0.1f)),
            SizeOverLife = ParticleVector.Curve([new ParticleVectorKey(0f, Vector3.One), new ParticleVectorKey(0.5f, new Vector3(2f)), new ParticleVectorKey(1f, Vector3.Zero)]),
            Acceleration = new Vector3(0, 0, -9.81f),
            LocalSpace = true,
            Sheet = new ParticleSheet(2, 2, ParticleFloat.Curve([new ParticleKey(0f, 0f), new ParticleKey(1f, 4f)])),
        };
        var sparks = new ParticleEmitter("sparks", new Material("spark"))
        {
            DeathSpawn = new ParticleDeathSpawn("core", ParticleFloat.Constant(2f)),
        };
        return new ParticleEffect("P_Glow", [core, sparks]);
    }

    private static (string Output, ExportResult Result) Export(ParticleEffect effect, string target)
    {
        var output = Path.Join(Path.GetTempPath(), "GMConverter.PCFTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var result = new PCFExporter().Export(effect, new ExportContext(output, "fx", OptionValues.Empty) { TargetGameId = target });
        return (output, result);
    }

    private sealed class Vector3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 x, Vector3 y) => Vector3.Distance(x, y) <= tolerance;

        public int GetHashCode(Vector3 obj) => 0;
    }
}
