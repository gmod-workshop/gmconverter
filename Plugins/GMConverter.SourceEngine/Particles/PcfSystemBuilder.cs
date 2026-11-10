using System.Numerics;
using GMConverter.SDK.Particles;

namespace GMConverter.SourceEngine.Particles;

/// <summary>
/// Turns a <see cref="ParticleEffect"/> into Source particle system definitions. Each emitter becomes
/// a system of its own, started by the effect's system as a child; its delay becomes its emitters'
/// start time. What Source's operators can't express is approximated or left out, and listed in
/// <see cref="Warnings"/>.
/// </summary>
internal sealed class PcfSystemBuilder(bool viewModel = false)
{
    private const float _inchesPerMetre = 39.3701f;
    private const int _defaultMaxParticles = 64;
    private const int _maximumParticles = 5000;

    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>The system name each emitter is written as.</summary>
    public static string SystemName(ParticleEffect effect, ParticleEmitter emitter) =>
        effect.Emitters.Count == 1 ? effect.Name : $"{effect.Name}_{emitter.Name}";

    /// <summary>
    /// The file's root element, holding the effect's systems. <paramref name="materialFor"/> gives each
    /// emitter's VMT path relative to <c>materials/</c>; <paramref name="sheetFor"/> its sprite sheet plan, if any.
    /// </summary>
    public DmxElement Build(ParticleEffect effect, Func<ParticleEmitter, string> materialFor, Func<ParticleEmitter, SpriteSheetPlan?> sheetFor)
    {
        List<DmxElement> systems = [];
        List<DmxElement> children = [];
        foreach (var emitter in effect.Emitters)
        {
            if (Emitter(effect, emitter, materialFor(emitter), sheetFor(emitter)) is not { } system)
            {
                continue;
            }

            if (effect.Emitters.Count == 1)
            {
                systems.Add(system);
                continue;
            }

            systems.Add(system);
            children.Add(new DmxElement("DmeParticleChild", system.Name).Set("child", system).Set("delay", 0f));
        }

        if (effect.Emitters.Count > 1)
        {
            var parent = System(effect.Name, "editor/dummy.vmt", 0);
            parent.Set("children", children);
            systems.Insert(0, parent);
        }

        return new DmxElement("DmeElement", "untitled").Set("particleSystemDefinitions", systems);
    }

    private DmxElement? Emitter(ParticleEffect effect, ParticleEmitter emitter, string material, SpriteSheetPlan? sheet)
    {
        var name = SystemName(effect, emitter);
        void Warn(string message) => _warnings.Add($"{name}: {message}");

        if (emitter.Mesh is not null)
        {
            Warn("mesh particles are not written; Source draws particle models only from compiled MDLs.");
            return null;
        }

        if (emitter.DeathSpawn is { } deathSpawn)
        {
            if (DeathSchedule(effect, emitter, deathSpawn) is not { } scheduled)
            {
                Warn($"spawns where {deathSpawn.Emitter}'s particles die, which Source particle systems can't do; left out.");
                return null;
            }

            Warn($"spawns where {deathSpawn.Emitter}'s particles die, which Source can't do; written as emission on {deathSpawn.Emitter}'s schedule, delayed by its particles' lifetime, from where they start.");
            emitter = scheduled;
        }

        var emitters = Emitters(emitter, Warn);
        if (emitters.Count == 0)
        {
            Warn("releases no particles (no spawn rate or bursts); left out.");
            return null;
        }

        var system = System(name, material, MaxParticles(emitter));
        system.Set("emitters", emitters);
        system.Set("initializers", Initializers(emitter, sheet, Warn));
        system.Set("operators", Operators(emitter, Warn));
        system.Set("renderers", [Renderer(emitter, sheet, Warn)]);
        system.Set("children", Array.Empty<DmxElement>());
        if (emitter.VelocityOverLife is { } velocityOverLife && !IsOne(velocityOverLife))
        {
            Warn("velocity over life is not written; Source has no operator that scales velocity by age.");
        }

        if (emitter.Orbit is { } orbit && (VectorBounds(orbit.RotationRate) != (Vector3.Zero, Vector3.Zero) || VectorBounds(orbit.Rotation) != (Vector3.Zero, Vector3.Zero)))
        {
            Warn("orbit rotation is not written; only its offset is kept.");
        }

        if (emitter.UvOffsetOverLife is not null)
        {
            Warn("texture slide over life is not written; Source particles can't move a material's texture by particle age, so the texture is drawn at rest.");
        }

        return system;
    }

    // Particles released where another emitter's die follow that emitter's releases, one average
    // lifetime later, from where its particles start (exact when they don't move far).
    private static ParticleEmitter? DeathSchedule(ParticleEffect effect, ParticleEmitter emitter, ParticleDeathSpawn deathSpawn)
    {
        var parent = effect.Emitters.FirstOrDefault(other => other.Name == deathSpawn.Emitter);
        if (parent is null || parent.DeathSpawn is not null)
        {
            return null;
        }

        var count = deathSpawn.Count.Bounds();
        var perDeath = (count.Min + count.Max) / 2f;
        var (lifeMin, lifeMax) = parent.Lifetime.Bounds();
        return emitter with
        {
            DeathSpawn = null,
            DelaySeconds = parent.DelaySeconds + ((lifeMin + lifeMax) / 2f),
            DurationSeconds = parent.DurationSeconds,
            Cycles = parent.Cycles,
            SpawnRate = parent.SpawnRate is { } rate ? new ParticleFloat(
                [.. rate.Min.Select(key => key with { Value = key.Value * perDeath })],
                [.. rate.Max.Select(key => key with { Value = key.Value * perDeath })]) : null,
            Bursts = [.. parent.Bursts.Select(burst => burst with
            {
                MinCount = (int)MathF.Round(burst.MinCount * count.Min),
                MaxCount = (int)MathF.Round(burst.MaxCount * count.Max),
            })],
            StartLocation = emitter.StartLocation ?? parent.StartLocation,
        };
    }

    private DmxElement System(string name, string material, int maxParticles)
    {
        return new DmxElement("DmeParticleSystemDefinition", name)
            .Set("view model effect", viewModel)
            .Set("preventNameBasedLookup", false)
            .Set("max_particles", maxParticles)
            .Set("initial_particles", 0)
            .Set("material", material)
            .Set("bounding_box_min", new Vector3(-10f))
            .Set("bounding_box_max", new Vector3(10f))
            .Set("cull_radius", 0f)
            .Set("cull_cost", 1f)
            .Set("cull_control_point", 0)
            .Set("cull_replacement_definition", string.Empty)
            .Set("radius", 5f)
            .SetColor("color", 255, 255, 255, 255)
            .Set("rotation", 0f)
            .Set("rotation_speed", 0f)
            .Set("sequence_number", 0)
            .Set("sequence_number 1", 0)
            .Set("group id", 0)
            .Set("maximum time step", 0.1f)
            .Set("maximum sim tick rate", 0f)
            .Set("minimum sim tick rate", 0f)
            .Set("minimum rendered frames", 0)
            .Set("control point to disable rendering if it is the camera", -1)
            .Set("maximum draw distance", 100000f)
            .Set("time to sleep when not drawn", 8f);
    }

    private static List<DmxElement> Emitters(ParticleEmitter emitter, Action<string> warn)
    {
        List<DmxElement> emitters = [];
        var cycles = emitter.Cycles <= 0 ? 1 : emitter.Cycles;
        if (emitter.Cycles <= 0 && emitter.Bursts.Count > 0)
        {
            warn("repeats forever; its bursts are written for the first cycle only.");
        }

        if (emitter.SpawnRate is { } rate)
        {
            var average = Average(rate, emitter.DurationSeconds);
            if (rate.IsAnimated || rate.IsRandom)
            {
                warn($"spawn rate varies; written as its average, {average:0.##} per second.");
            }

            if (average > 0f)
            {
                var duration = emitter.Cycles <= 0 || emitter.DurationSeconds <= 0f ? 0f : emitter.DurationSeconds * cycles;
                emitters.Add(Operator("emit_continuously")
                    .Set("emission_start_time", emitter.DelaySeconds)
                    .Set("emission_rate", average)
                    .Set("emission_duration", duration));
            }
        }

        for (var cycle = 0; cycle < cycles; cycle++)
        {
            foreach (var burst in emitter.Bursts.Where(burst => burst.MaxCount > 0))
            {
                emitters.Add(Operator("emit_instantaneously")
                    .Set("emission_start_time", emitter.DelaySeconds + (cycle * emitter.DurationSeconds) + burst.TimeSeconds)
                    .Set("num_to_emit", burst.MaxCount)
                    .Set("num_to_emit_minimum", burst.MinCount == burst.MaxCount ? -1 : burst.MinCount)
                    .Set("maximum emission per frame", -1));
            }
        }

        return emitters;
    }

    private static List<DmxElement> Initializers(ParticleEmitter emitter, SpriteSheetPlan? sheet, Action<string> warn)
    {
        var (lifeMin, lifeMax) = emitter.Lifetime.Bounds();
        if (emitter.Lifetime.IsAnimated)
        {
            warn("lifetime changes over the emitter's cycle; written as its full range.");
        }

        var radial = emitter.RadialVelocity?.Bounds() ?? (Min: 0f, Max: 0f);
        List<DmxElement> initializers =
        [
            Operator("Lifetime Random").Set("lifetime_min", lifeMin).Set("lifetime_max", lifeMax),
            Operator("Position Within Sphere Random")
                .Set("distance_min", 0f)
                .Set("distance_max", 0f)
                .Set("speed_min", radial.Min * _inchesPerMetre)
                .Set("speed_max", radial.Max * _inchesPerMetre)
                .Set("control_point_number", 0),
        ];

        var offset = (Min: Vector3.Zero, Max: Vector3.Zero);
        if (emitter.StartLocation is { } location)
        {
            offset = VectorBounds(location);
        }

        if (emitter.Orbit is { } orbit)
        {
            var orbitOffset = VectorBounds(orbit.Offset);
            offset = (offset.Min + orbitOffset.Min, offset.Max + orbitOffset.Max);
        }

        if (offset != (Vector3.Zero, Vector3.Zero))
        {
            initializers.Add(Operator("Position Modify Offset Random")
                .Set("offset min", Inches(offset.Min))
                .Set("offset max", Inches(offset.Max))
                .Set("offset in local space 0/1", true)
                .Set("control_point_number", 0));
        }

        if (emitter.StartVelocity is { } velocity)
        {
            var (min, max) = VectorBounds(velocity);
            initializers.Add(Operator("Velocity Random")
                .Set("speed_in_local_coordinate_system_min", Inches(min))
                .Set("speed_in_local_coordinate_system_max", Inches(max))
                .Set("control_point_number", 0));
        }

        var (sizeMin, sizeMax) = VectorBounds(emitter.StartSize);
        if (MathF.Abs(sizeMax.X - sizeMax.Y) > 0.05f * MathF.Max(sizeMax.X, sizeMax.Y))
        {
            warn("sprites are not square; written with their width.");
        }

        initializers.Add(Operator("Radius Random")
            .Set("radius_min", sizeMin.X * 0.5f * _inchesPerMetre)
            .Set("radius_max", sizeMax.X * 0.5f * _inchesPerMetre));

        var color = emitter.ColorOverLife is { } colorOverLife ? AtStart(colorOverLife) : VectorBounds(emitter.StartColor);
        var brightness = Brightness(emitter);
        var (low, high) = (color.Min / brightness, color.Max / brightness);
        initializers.Add(Operator("Color Random")
            .SetColor("color1", Srgb(low.X), Srgb(low.Y), Srgb(low.Z), 255)
            .SetColor("color2", Srgb(high.X), Srgb(high.Y), Srgb(high.Z), 255));

        var (alphaMin, alphaMax) = emitter.AlphaOverLife is { } alphaOverLife
            ? (alphaOverLife.Bounds().Max, alphaOverLife.Bounds().Max)
            : emitter.StartAlpha.Bounds();
        initializers.Add(Operator("Alpha Random")
            .Set("alpha_min", (int)MathF.Round(Math.Clamp(alphaMin, 0f, 1f) * 255f))
            .Set("alpha_max", (int)MathF.Round(Math.Clamp(alphaMax, 0f, 1f) * 255f)));

        if (emitter.StartRotation is { } rotation)
        {
            var (min, max) = rotation.Bounds();
            initializers.Add(Operator("Rotation Random")
                .Set("rotation_initial", 0f)
                .Set("rotation_offset_min", Degrees(min))
                .Set("rotation_offset_max", Degrees(max)));
        }

        if (emitter.RotationRate is { } spin)
        {
            var (min, max) = spin.Bounds();
            initializers.Add(Operator("Rotation Speed Random")
                .Set("rotation_speed_constant", 0f)
                .Set("rotation_speed_random_min", Degrees(min))
                .Set("rotation_speed_random_max", Degrees(max)));
        }

        if (sheet is { RandomSequence: true })
        {
            initializers.Add(Operator("Sequence Random").Set("sequence_min", 0).Set("sequence_max", sheet.Sequences.Count - 1));
        }

        return initializers;
    }

    private static List<DmxElement> Operators(ParticleEmitter emitter, Action<string> warn)
    {
        List<DmxElement> operators =
        [
            Operator("Movement Basic")
                .Set("gravity", Inches(emitter.Acceleration))
                .Set("drag", Math.Clamp(emitter.Drag, 0f, 1f))
                .Set("max constraint passes", 3),
            Operator("Lifespan Decay"),
        ];

        if (emitter.LocalSpace)
        {
            operators.Add(Operator("Movement Lock to Control Point").Set("control_point_number", 0));
        }

        if (emitter.RotationRate is not null)
        {
            operators.Add(Operator("Rotation Basic"));
        }

        if (emitter.SizeOverLife is { } size)
        {
            if (size.IsRandom)
            {
                warn("size over life is random; written as the middle of its range.");
            }

            operators.AddRange(RadiusScales(size.Component(0)));
        }

        if (emitter.AlphaOverLife is { } alpha)
        {
            operators.AddRange(AlphaFades(alpha, warn));
        }

        if (emitter.ColorOverLife is { IsAnimated: true } color)
        {
            var keys = color.Min.Count;
            if (keys > 2)
            {
                warn($"colour over life has {keys} keys; written as a fade from the first to the last.");
            }

            var end = color.Evaluate(1f, new Vector3(0.5f)) / Brightness(emitter);
            operators.Add(Operator("Color Fade")
                .SetColor("color_fade", Srgb(end.X), Srgb(end.Y), Srgb(end.Z), 255)
                .Set("fade_start_time", color.Min[0].Time)
                .Set("fade_end_time", color.Min[^1].Time)
                .Set("ease_in_and_out", false));
        }

        return operators;
    }

    // Radius Scale scales the starting radius inside its window, so one per curve segment follows the curve exactly.
    private static IEnumerable<DmxElement> RadiusScales(ParticleFloat scale)
    {
        if (!scale.IsAnimated)
        {
            var constant = scale.Evaluate(0f, 0.5f);
            if (MathF.Abs(constant - 1f) > 1e-4f)
            {
                yield return RadiusScale(0f, 1f, constant, constant);
            }

            yield break;
        }

        var times = scale.Min.Select(key => key.Time).Concat(scale.Max.Select(key => key.Time)).Append(0f).Append(1f)
            .Select(time => Math.Clamp(time, 0f, 1f)).Distinct().Order().ToArray();
        for (var index = 1; index < times.Length; index++)
        {
            yield return RadiusScale(times[index - 1], times[index], scale.Evaluate(times[index - 1], 0.5f), scale.Evaluate(times[index], 0.5f));
        }
    }

    private static DmxElement RadiusScale(float start, float end, float from, float to) => Operator("Radius Scale")
        .Set("start_time", start)
        .Set("end_time", end)
        .Set("radius_start_scale", from)
        .Set("radius_end_scale", to)
        .Set("ease_in_and_out", false)
        .Set("scale_bias", 0.5f);

    // Source fades alpha in from 0 and out to 0; the curve's peak becomes the starting alpha.
    private static IEnumerable<DmxElement> AlphaFades(ParticleFloat alpha, Action<string> warn)
    {
        var peak = alpha.Bounds().Max;
        if (peak <= 0f)
        {
            yield break;
        }

        const int samples = 64;
        var values = Enumerable.Range(0, samples + 1).Select(step => alpha.Evaluate(step / (float)samples, 0.5f) / peak).ToArray();
        var firstPeak = Array.FindIndex(values, value => value >= 0.98f) / (float)samples;
        var lastPeak = Array.FindLastIndex(values, value => value >= 0.98f) / (float)samples;
        // The trapezoid Source will draw: a linear fade in to the first peak, out from the last.
        var shaped = values.Select((_, step) =>
        {
            var time = step / (float)samples;
            return time < firstPeak ? time / firstPeak
                : time > lastPeak ? (1f - time) / (1f - lastPeak)
                : 1f;
        }).ToArray();
        if (values.Zip(shaped).Any(pair => MathF.Abs(pair.First - pair.Second) > 0.2f))
        {
            warn("alpha over life is written as a fade in and out, which doesn't match its curve closely.");
        }

        if (firstPeak > 0f && values[0] < 0.98f)
        {
            yield return Operator("Alpha Fade In Random")
                .Set("fade in time min", firstPeak)
                .Set("fade in time max", firstPeak)
                .Set("fade in time exponent", 1f)
                .Set("proportional 0/1", true);
        }

        if (lastPeak < 1f && values[^1] < 0.98f)
        {
            yield return Operator("Alpha Fade Out Random")
                .Set("fade out time min", 1f - lastPeak)
                .Set("fade out time max", 1f - lastPeak)
                .Set("fade out time exponent", 1f)
                .Set("fade bias", 0.5f)
                .Set("ease in and out", false)
                .Set("proportional 0/1", true);
        }
    }

    private static DmxElement Renderer(ParticleEmitter emitter, SpriteSheetPlan? sheet, Action<string> warn)
    {
        if (emitter.Alignment == ParticleAlignment.Velocity)
        {
            warn("velocity-aligned sprites are written as sprite trails, stretched by speed.");
            return Operator("render_sprite_trail")
                .Set("animation rate", 1f)
                .Set("length fade in time", 0f)
                .Set("max length", 2000f)
                .Set("min length", 0f);
        }

        return Operator("render_animated_sprites")
            .Set("animation rate", 1f)
            .Set("animation_fit_lifetime", sheet is { RandomSequence: false })
            .Set("orientation_type", 0)
            .Set("use animation rate as FPS", false)
            .Set("second sequence animation rate", 0f);
    }

    private static DmxElement Operator(string function) =>
        new DmxElement("DmeParticleOperator", function).Set("functionName", function);

    private static int MaxParticles(ParticleEmitter emitter)
    {
        if (emitter.MaxParticles is > 0 and var max)
        {
            return Math.Min(max, _maximumParticles);
        }

        var lifetime = emitter.Lifetime.Bounds().Max;
        var rate = emitter.SpawnRate?.Bounds().Max ?? 0f;
        var bursts = emitter.Bursts.Sum(burst => burst.MaxCount);
        var estimate = (int)MathF.Ceiling((rate * lifetime) + bursts);
        return estimate > 0 ? Math.Min(estimate + 4, _maximumParticles) : _defaultMaxParticles;
    }

    private static float Average(ParticleFloat rate, float duration)
    {
        const int samples = 32;
        var span = duration > 0f ? duration : 1f;
        return Enumerable.Range(0, samples).Average(step => rate.Evaluate((step + 0.5f) / samples * span, 0.5f));
    }

    private static (Vector3 Min, Vector3 Max) VectorBounds(ParticleVector value)
    {
        var keys = value.Min.Concat(value.Max).Select(key => key.Value).ToArray();
        return keys.Length == 0 ? (Vector3.Zero, Vector3.Zero) : (keys.Aggregate(Vector3.Min), keys.Aggregate(Vector3.Max));
    }

    private static (Vector3 Min, Vector3 Max) AtStart(ParticleVector value) => (value.Evaluate(0f, Vector3.Zero), value.Evaluate(0f, Vector3.One));

    private static bool IsOne(ParticleVector value) => value.Min.Concat(value.Max).All(key => key.Value == Vector3.One);

    private static Vector3 Inches(Vector3 metres) => metres * _inchesPerMetre;

    private static float Degrees(float radians) => radians * 180f / MathF.PI;

    /// <summary>
    /// How much brighter than white an emitter's colours get (1 when they don't). Source's particle
    /// colours stop at white, so the exporter multiplies the emitter's texture by this and the colours
    /// are divided by it: the same product, as additive glows tinted far past white need.
    /// </summary>
    public static float Brightness(ParticleEmitter emitter)
    {
        var keys = emitter.StartColor.Min.Concat(emitter.StartColor.Max)
            .Concat(emitter.ColorOverLife is { } overLife ? overLife.Min.Concat(overLife.Max) : []);
        return Math.Max(1f, keys.Max(key => MathF.Max(key.Value.X, MathF.Max(key.Value.Y, key.Value.Z))));
    }

    private static byte Srgb(float linear)
    {
        var value = Math.Clamp(linear, 0f, 1f);
        var encoded = value <= 0.0031308f ? value * 12.92f : (1.055f * MathF.Pow(value, 1f / 2.4f)) - 0.055f;
        return (byte)MathF.Round(encoded * 255f);
    }
}
