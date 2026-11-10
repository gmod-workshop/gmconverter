using System.Numerics;
using GMConverter.Core.Textures;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Particles;
using GMConverter.UnrealEngine.Formats.UE3.Materials;
using GMConverter.UnrealEngine.Formats.UE3.Particles;
using GMConverter.UnrealEngine.Importers;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UE3ParticleTests
{
    [Fact]
    public void LookupTablesSampleEvenlyAfterTheirRange()
    {
        // Output range 0..1, then five samples a quarter apart: a rise and fall.
        float[] table = [0f, 1f, 0f, 0.5f, 1f, 0.5f, 0f];

        var alpha = UE3Distribution.FloatFromTable(table, new UE3DistributionTable(Random: false, Chunk: 1, StartTime: 0f, TimeScale: 4f));

        Assert.Equal([0f, 0.25f, 0.5f, 0.75f, 1f], alpha.Min.Select(key => key.Time));
        Assert.Equal(1f, alpha.Evaluate(0.5f));
        Assert.False(alpha.IsRandom);
    }

    [Fact]
    public void RandomVectorTablesHoldMinAndMaxAndStayOrderedWhenMirrored()
    {
        // A uniform size from 6 to 12.5 per axis, baked as two identical samples with no time scale.
        float[] table = [6f, 12.5f, 6f, 6f, 6f, 12.5f, 12.5f, 12.5f, 6f, 6f, 6f, 12.5f, 12.5f, 12.5f];

        var size = UE3Distribution.VectorFromTable(table, new UE3DistributionTable(Random: true, Chunk: 6, StartTime: 0f, TimeScale: 0f), value => value with { Y = -value.Y });

        var key = Assert.Single(size.Min);
        Assert.Equal(new Vector3(6f, -12.5f, 6f), key.Value);
        Assert.Equal(new Vector3(12.5f, -6f, 12.5f), Assert.Single(size.Max).Value);
    }

    [Fact]
    public void OmittedValuesTakeTheEnginesDefaultSubObjects()
    {
        Assert.Equal(1f, UE3DistributionDefaults.Find<float>("Engine.Default__ParticleModuleSpawn.RequiredDistributionSpawnRateScale", "Constant"));
        Assert.Equal(20f, UE3DistributionDefaults.Find<float>("Engine.Default__ParticleModuleSpawn.RequiredDistributionSpawnRate", "Constant"));
        Assert.Null(UE3DistributionDefaults.Find<float>("Engine.Default__ParticleModuleLifetime.DistributionLifetime", "Min"));
    }

    [Fact]
    public void SidecarsImportWithTheirTexturesAndBlend()
    {
        var directory = PskFixtures.CreateDirectory();
        new DefaultTextureFactory().FromRgba("flare", 4, 4, Enumerable.Repeat((byte)255, 64).ToArray(), hasAlpha: true).WritePng(Path.Join(directory, "flare.png"));
        var settings = new ParticleEmitter("core", new Material("unused"))
        {
            DurationSeconds = 0.2f,
            SpawnRate = ParticleFloat.Constant(60f),
            Lifetime = ParticleFloat.Range(0.1f, 0.2f),
            Sheet = new ParticleSheet(2, 2, null),
            StartSize = ParticleVector.Range(new Vector3(0.06f), new Vector3(0.125f)),
        };
        var (psk, _) = PskFixtures.WriteFixture();
        File.Copy(psk, Path.Join(directory, "SM_Arc.psk"));
        var path = Path.Join(directory, "P_Flare" + UE3ParticleSidecar.Extension);
        new UE3ParticleSidecar("P_Flare",
        [
            new UE3ParticleSidecar.Emitter(settings, "flare.png", MaterialBlendMode.Additive, null),
            new UE3ParticleSidecar.Emitter(settings with { Name = "arc" }, null, MaterialBlendMode.AlphaBlend, "SM_Arc.psk") { UvScale = new Vector2(1f, 3f) },
        ]).Write(path);

        var effect = new UE3ParticleImporter(new DefaultTextureFactory()).Import(new ParticleImportContext(path));

        Assert.Equal("P_Flare", effect.Name);
        var core = effect.Emitters[0];
        Assert.Equal(MaterialBlendMode.Additive, core.Material.BlendMode);
        Assert.Equal(4, core.Material.DiffuseTexture?.Width);
        Assert.Equal((0.1f, 0.2f), core.Lifetime.Bounds());
        Assert.Equal(4, core.Sheet?.FrameCount);
        Assert.Equal(new Vector3(0.125f), core.StartSize.Evaluate(0f, Vector3.One));
        Assert.Null(core.Mesh);
        var arc = effect.Emitters[1];
        Assert.Equal("SM_Arc", arc.Mesh?.Name);
        Assert.NotEmpty(arc.Mesh!.Meshes);
        Assert.Equal(new Vector2(1f, 3f), arc.Material.UvTransform?.Scale);
    }

    [Fact]
    public void SidecarsCarryMaterialGraphsAndParameters()
    {
        var directory = PskFixtures.CreateDirectory();
        new DefaultTextureFactory().FromRgba("helix", 2, 2, Enumerable.Repeat((byte)128, 16).ToArray(), hasAlpha: true).WritePng(Path.Join(directory, "helix.png"));
        // The helix glow: texture at UV + parameter.x * (0, -1), tinted by the particle colour.
        MaterialGraphNode[] nodes =
        [
            new(MaterialGraphOp.TexCoord, []),
            new(MaterialGraphOp.ParticleParameters, []),
            new(MaterialGraphOp.Swizzle, [1]) { Components = "x" },
            MaterialGraphNode.Constant(new Vector4(0f, -1f, 0f, 0f), 2),
            new(MaterialGraphOp.Multiply, [2, 3]),
            new(MaterialGraphOp.Add, [0, 4]),
            new(MaterialGraphOp.TextureSample, [5]) { Texture = 0 },
            new(MaterialGraphOp.ParticleColor, []),
            new(MaterialGraphOp.Multiply, [6, 7]),
            new(MaterialGraphOp.Swizzle, [8]) { Components = "xyz" },
        ];
        var settings = new ParticleEmitter("helix", new Material("unused"))
        {
            Parameters = [ParticleFloat.Curve([new ParticleKey(0f, -1f), new ParticleKey(1f, 3f)])],
        };
        var path = Path.Join(directory, "P_Helix" + UE3ParticleSidecar.Extension);
        new UE3ParticleSidecar("P_Helix",
        [
            new UE3ParticleSidecar.Emitter(settings, null, MaterialBlendMode.Additive, null)
            {
                Graph = new UE3MaterialGraphFile(nodes, [new UE3MaterialGraphFile.TextureFile("helix.png", Srgb: true, ClampU: false, ClampV: true)], Emissive: 9, Opacity: null),
            },
        ]).Write(path);

        var emitter = Assert.Single(new UE3ParticleImporter(new DefaultTextureFactory()).Import(new ParticleImportContext(path)).Emitters);

        var graph = emitter.Material.Graph!;
        Assert.Equal(nodes, graph.Nodes, (a, b) => a.Op == b.Op && a.Inputs.SequenceEqual(b.Inputs) && a.Value == b.Value && a.Size == b.Size && a.Components == b.Components && a.Texture == b.Texture);
        Assert.Equal(9, graph.Emissive);
        Assert.Equal(3, graph.SizeOf(9));
        var texture = Assert.Single(graph.Textures);
        Assert.Equal((2, true, false, true), (texture.Texture.Width, texture.Srgb, texture.ClampU, texture.ClampV));
        Assert.Equal(3f, Assert.Single(emitter.Parameters)!.Evaluate(1f));
    }
}
