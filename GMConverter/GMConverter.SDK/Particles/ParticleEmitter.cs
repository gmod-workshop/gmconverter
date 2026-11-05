using System.Numerics;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;

namespace GMConverter.SDK.Particles;

/// <summary>
/// One stream of particles in a <see cref="ParticleEffect"/>: when and how many it releases, how each
/// particle starts, and how it changes over its life. Distances are metres, times seconds and angles
/// radians, in the effect's space (+X forward, +Y left, +Z up). "At spawn" values are evaluated at the
/// emitter's time when the particle is released; "over life" values run from 0 at birth to 1 at death
/// and multiply the particle's starting value unless they say otherwise.
/// </summary>
/// <param name="Name">The emitter's name, unique in its effect.</param>
/// <param name="Material">What each particle draws: its diffuse texture, and blend mode (additive glows, alpha-blended smoke).</param>
public sealed record ParticleEmitter(string Name, Material Material)
{
    /// <summary>How each sprite faces; ignored for <see cref="Mesh"/> particles.</summary>
    public ParticleAlignment Alignment { get; init; } = ParticleAlignment.Camera;

    /// <summary>A model each particle draws instead of a sprite, or null for sprites.</summary>
    public Model? Mesh { get; init; }

    /// <summary>A flipbook of frames in the material's texture, or null for a plain texture.</summary>
    public ParticleSheet? Sheet { get; init; }

    /// <summary>Seconds after the effect starts before the emitter does.</summary>
    public float DelaySeconds { get; init; }

    /// <summary>How long one cycle of the emitter lasts; 0 for an emitter that only bursts.</summary>
    public float DurationSeconds { get; init; }

    /// <summary>How many cycles it runs, or 0 to repeat until the effect is stopped.</summary>
    public int Cycles { get; init; } = 1;

    /// <summary>Particles released per second, over the emitter's cycle; null releases none continuously.</summary>
    public ParticleFloat? SpawnRate { get; init; }

    /// <summary>Particles released at once during each cycle.</summary>
    public IReadOnlyList<ParticleBurst> Bursts { get; init; } = [];

    /// <summary>Particles released where another emitter's particles die, instead of by rate or burst.</summary>
    public ParticleDeathSpawn? DeathSpawn { get; init; }

    /// <summary>The most particles alive at once, when the source game caps it.</summary>
    public int? MaxParticles { get; init; }

    /// <summary>Whether particles move with the effect after spawning (true) or stay where they were released.</summary>
    public bool LocalSpace { get; init; }

    /// <summary>How long each particle lives, at spawn.</summary>
    public ParticleFloat Lifetime { get; init; } = ParticleFloat.Constant(1f);

    /// <summary>Where each particle starts relative to the effect, at spawn.</summary>
    public ParticleVector? StartLocation { get; init; }

    /// <summary>Each particle's starting size (X across, Y up the sprite; Z for meshes), at spawn.</summary>
    public ParticleVector StartSize { get; init; } = ParticleVector.Constant(new Vector3(0.1f));

    /// <summary>Scales each particle's size over its life.</summary>
    public ParticleVector? SizeOverLife { get; init; }

    /// <summary>Each particle's starting colour, linear RGB, at spawn.</summary>
    public ParticleVector StartColor { get; init; } = ParticleVector.Constant(Vector3.One);

    /// <summary>Each particle's starting opacity, 0 to 1, at spawn.</summary>
    public ParticleFloat StartAlpha { get; init; } = ParticleFloat.Constant(1f);

    /// <summary>Each particle's colour over its life; replaces <see cref="StartColor"/> when set.</summary>
    public ParticleVector? ColorOverLife { get; init; }

    /// <summary>Each particle's opacity over its life; replaces <see cref="StartAlpha"/> when set.</summary>
    public ParticleFloat? AlphaOverLife { get; init; }

    /// <summary>Each particle's starting velocity, metres per second, at spawn.</summary>
    public ParticleVector? StartVelocity { get; init; }

    /// <summary>Speed away from the effect's origin added at spawn, metres per second.</summary>
    public ParticleFloat? RadialVelocity { get; init; }

    /// <summary>Scales each particle's velocity over its life.</summary>
    public ParticleVector? VelocityOverLife { get; init; }

    /// <summary>Constant acceleration, metres per second squared (gravity is -9.8 Z).</summary>
    public Vector3 Acceleration { get; init; }

    /// <summary>How much of its velocity each particle loses per second, 0 for none.</summary>
    public float Drag { get; init; }

    /// <summary>Each sprite's starting roll, at spawn.</summary>
    public ParticleFloat? StartRotation { get; init; }

    /// <summary>How fast each sprite rolls, radians per second, at spawn.</summary>
    public ParticleFloat? RotationRate { get; init; }

    /// <summary>
    /// Slides the material's texture over each particle's life, in texture widths (X, along U) and
    /// heights (Y, along V); Z is unused. Applied after the material's own
    /// <see cref="Material.UvTransform"/>. Effects whose glow travels along a mesh use it.
    /// </summary>
    public ParticleVector? UvOffsetOverLife { get; init; }

    /// <summary>Moves each particle's drawn position around its real one.</summary>
    public ParticleOrbit? Orbit { get; init; }
}
