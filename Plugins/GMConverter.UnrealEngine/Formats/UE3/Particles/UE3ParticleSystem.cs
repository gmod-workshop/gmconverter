using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Particles;
using GMConverter.UnrealEngine.Formats.UE3.Materials;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Particles;

/// <summary>
/// A Cascade particle system read into SDK emitters (from each emitter's first LOD level), with the
/// material each draws, which the exporter bakes. Distances become metres in the SDK's space
/// (Unreal's Y flipped), rotations radians. Modules with no SDK counterpart are named in
/// <see cref="Skipped"/>.
/// </summary>
internal sealed record UE3ParticleSystem(string Name, IReadOnlyList<UE3ParticleEmitter> Emitters, IReadOnlyList<string> Skipped)
{
    private const float _metresPerUnit = 0.01f;
    private const float _radiansPerTurn = MathF.PI * 2f;

    // Modules that change only how Unreal draws or culls a particle, which nothing downstream uses.
    private static readonly HashSet<string> _ignored = new(StringComparer.Ordinal)
    {
        "ParticleModuleEventGenerator", "ParticleModuleCameraOffset", "ParticleModuleTypeDataMesh",
    };

    public static UE3ParticleSystem Read(UE3GameContent content, UE3Package package, UE3PackageExport export)
    {
        var properties = package.OpenObject(export).Properties;
        List<(UE3PackageExport Export, UE3Properties Lod)> emitters = [];
        foreach (var emitterIndex in properties.GetObjectArray("Emitters"))
        {
            if (package.GetExport(emitterIndex) is not { } emitter ||
                package.OpenObject(emitter).Properties.GetObjectArray("LODLevels") is not { Count: > 0 } lods ||
                package.GetExport(lods[0]) is not { } lod)
            {
                continue;
            }

            var lodProperties = package.OpenObject(lod).Properties;
            if (lodProperties.GetBool("bEnabled") == false)
            {
                continue;
            }

            emitters.Add((emitter, lodProperties));
        }

        // Event receivers spawn from the emitter whose generator raises the event they listen for.
        Dictionary<string, string> eventSources = new(StringComparer.OrdinalIgnoreCase);
        var names = emitters.Select((emitter, index) => EmitterName(package, emitter.Export, emitter.Lod, index)).ToArray();
        for (var index = 0; index < emitters.Count; index++)
        {
            if (package.GetExport(emitters[index].Lod.GetObject("EventGenerator")) is { } generator)
            {
                foreach (var generated in package.OpenObject(generator).Properties.GetStructArray("Events", package.Summary.FileVersion))
                {
                    if (generated.GetName("CustomName") is { Length: > 0 } eventName)
                    {
                        eventSources.TryAdd(eventName, names[index]);
                    }
                }
            }
        }

        List<string> skipped = [];
        List<UE3ParticleEmitter> read = [];
        for (var index = 0; index < emitters.Count; index++)
        {
            read.Add(ReadEmitter(content, package, emitters[index].Lod, names[index], eventSources, skipped));
        }

        return new UE3ParticleSystem(export.ObjectName, read, [.. skipped.Distinct(StringComparer.Ordinal)]);
    }

    private static string EmitterName(UE3Package package, UE3PackageExport emitter, UE3Properties lod, int index)
    {
        var name = package.OpenObject(emitter).Properties.GetName("EmitterName");
        return name is { Length: > 0 } and not "Particle Emitter" ? NameHelpers.SanitizeFileName(name) : FormattableString.Invariant($"emitter{index + 1}");
    }

    private static UE3ParticleEmitter ReadEmitter(
        UE3GameContent content, UE3Package package, UE3Properties lod, string name, Dictionary<string, string> eventSources, List<string> skipped)
    {
        UE3Properties Module(int index) => package.OpenObject(package.GetExport(index)!).Properties;
        var required = package.GetExport(lod.GetObject("RequiredModule")) is { } requiredExport ? package.OpenObject(requiredExport).Properties : null;
        var emitter = new ParticleEmitter(name, new Material(name));
        UE3ObjectReference? material = null;
        UE3ObjectReference? mesh = null;
        UE3ObjectReference? meshMaterial = null;
        var dynamicParameters = new ParticleFloat?[4];

        if (required is not null)
        {
            var columns = required.GetInt("SubImages_Horizontal") ?? 1;
            var rows = required.GetInt("SubImages_Vertical") ?? 1;
            emitter = emitter with
            {
                DurationSeconds = required.GetFloat("EmitterDuration") ?? 1f,
                Cycles = required.GetInt("EmitterLoops") ?? 0,
                DelaySeconds = required.GetFloat("EmitterDelay") ?? 0f,
                LocalSpace = required.GetBool("bUseLocalSpace") ?? false,
                Alignment = required.GetName("ScreenAlignment") == "PSA_Velocity" ? ParticleAlignment.Velocity : ParticleAlignment.Camera,
                MaxParticles = required.GetBool("bUseMaxDrawCount") == true ? required.GetInt("MaxDrawCount") : null,
                Sheet = columns * rows > 1 ? new ParticleSheet(columns, rows, null) : null,
            };
            if (content.Resolve(package, required.GetObject("Material")) is { } found)
            {
                material = new UE3ObjectReference(found.Package, found.Export);
            }
        }

        if (package.GetExport(lod.GetObject("TypeDataModule")) is { } typeData && package.GetClassName(typeData) == "ParticleModuleTypeDataMesh")
        {
            mesh = content.Resolve(package, package.OpenObject(typeData).Properties.GetObject("Mesh")) is { } meshExport
                ? new UE3ObjectReference(meshExport.Package, meshExport.Export)
                : null;
        }

        if (package.GetExport(lod.GetObject("SpawnModule")) is { } spawnExport)
        {
            var spawn = package.OpenObject(spawnExport).Properties;
            var rate = UE3Distribution.ReadFloat(package, spawn, "Rate");
            var scale = UE3Distribution.ReadFloat(package, spawn, "RateScale")?.Evaluate(0f, 0.5f) ?? 1f;
            var duration = emitter.DurationSeconds;
            emitter = emitter with
            {
                SpawnRate = rate is null ? null : Scale(rate, scale),
                // Burst times are fractions of the emitter's duration.
                Bursts = [.. spawn.GetStructArray("BurstList", package.Summary.FileVersion).Select(burst =>
                {
                    var count = burst.GetInt("Count") ?? 0;
                    var low = burst.GetInt("CountLow") ?? -1;
                    return new ParticleBurst((burst.GetFloat("Time") ?? 0f) * duration, low >= 0 ? Math.Min(low, count) : count, count);
                })],
            };
        }

        foreach (var moduleIndex in lod.GetObjectArray("Modules"))
        {
            if (package.GetExport(moduleIndex) is not { } moduleExport)
            {
                continue;
            }

            var module = Module(moduleIndex);
            if (module.GetBool("bEnabled") == false)
            {
                continue;
            }

            var className = package.GetClassName(moduleExport);
            ParticleFloat? Float(string property) => UE3Distribution.ReadFloat(package, module, property);
            ParticleVector? Distance(string property) => UE3Distribution.ReadVector(package, module, property, ToMetres);
            ParticleVector? Plain(string property) => UE3Distribution.ReadVector(package, module, property, value => value);
            switch (className)
            {
                case "ParticleModuleLifetime":
                    emitter = emitter with { Lifetime = Float("Lifetime") ?? emitter.Lifetime };
                    break;
                // A mesh particle's size scales its mesh; a sprite's is a length.
                case "ParticleModuleSize":
                    emitter = emitter with { StartSize = Map(Plain("StartSize"), value => mesh is null ? value * _metresPerUnit : value) ?? emitter.StartSize };
                    break;
                case "ParticleModuleSizeMultiplyLife":
                case "ParticleModuleSizeScale":
                    emitter = emitter with { SizeOverLife = Plain(className.EndsWith("Life", StringComparison.Ordinal) ? "LifeMultiplier" : "SizeScale") };
                    break;
                case "ParticleModuleColor":
                    emitter = emitter with
                    {
                        StartColor = Plain("StartColor") ?? emitter.StartColor,
                        StartAlpha = Float("StartAlpha") ?? emitter.StartAlpha,
                    };
                    break;
                case "ParticleModuleColorOverLife":
                    emitter = emitter with { ColorOverLife = Plain("ColorOverLife"), AlphaOverLife = Float("AlphaOverLife") };
                    break;
                case "ParticleModuleLocation":
                    emitter = emitter with { StartLocation = Distance("StartLocation") };
                    break;
                case "ParticleModuleVelocity":
                    emitter = emitter with
                    {
                        StartVelocity = Distance("StartVelocity"),
                        RadialVelocity = Float("StartVelocityRadial") is { } radial ? Scale(radial, _metresPerUnit) : null,
                    };
                    break;
                case "ParticleModuleVelocityOverLifetime":
                    emitter = emitter with { VelocityOverLife = Plain("VelOverLife") };
                    break;
                case "ParticleModuleAcceleration":
                    emitter = emitter with { Acceleration = Distance("Acceleration")?.Evaluate(0f, new Vector3(0.5f)) ?? Vector3.Zero };
                    break;
                // Mirroring Unreal's Y reverses which way a sprite rolls.
                case "ParticleModuleRotation":
                    emitter = emitter with { StartRotation = Float("StartRotation") is { } rotation ? Scale(rotation, -_radiansPerTurn) : null };
                    break;
                case "ParticleModuleRotationRate":
                    emitter = emitter with { RotationRate = Float("StartRotationRate") is { } rate ? Scale(rate, -_radiansPerTurn) : null };
                    break;
                case "ParticleModuleSubUV":
                    if (emitter.Sheet is { } sheet)
                    {
                        var random = required?.GetName("InterpolationMethod") is "PSUVIM_Random" or "PSUVIM_Random_Blend";
                        emitter = emitter with { Sheet = sheet with { FrameOverLife = random ? null : Float("SubImageIndex") } };
                    }

                    break;
                case "ParticleModuleOrbit":
                    emitter = emitter with
                    {
                        Orbit = new ParticleOrbit(
                            Distance("OffsetAmount") ?? ParticleVector.Constant(Vector3.Zero),
                            Map(Plain("RotationAmount"), value => value * _radiansPerTurn) ?? ParticleVector.Constant(Vector3.Zero),
                            Map(Plain("RotationRateAmount"), value => value * _radiansPerTurn) ?? ParticleVector.Constant(Vector3.Zero)),
                    };
                    break;
                case "ParticleModuleEventReceiverSpawn":
                    if (module.GetName("EventName") is { } eventName && eventSources.TryGetValue(eventName, out var source))
                    {
                        emitter = emitter with { DeathSpawn = new ParticleDeathSpawn(source, Float("SpawnCount") ?? ParticleFloat.Constant(1f)) };
                    }

                    break;
                case "ParticleModuleMeshMaterial":
                    if (module.GetObjectArray("MeshMaterials") is { Count: > 0 } meshMaterials && content.Resolve(package, meshMaterials[0]) is { } meshMaterialExport)
                    {
                        meshMaterial = new UE3ObjectReference(meshMaterialExport.Package, meshMaterialExport.Export);
                    }

                    break;
                // Four values per particle that its material reads, usually over its life.
                case "ParticleModuleParameterDynamic":
                    var parameters = module.GetStructArray("DynamicParams", package.Summary.FileVersion);
                    for (var channel = 0; channel < Math.Min(4, parameters.Count); channel++)
                    {
                        dynamicParameters[channel] = UE3Distribution.ReadFloat(package, parameters[channel], "ParamValue");
                    }

                    break;
                default:
                    if (!_ignored.Contains(className))
                    {
                        skipped.Add(className);
                    }

                    break;
            }
        }

        return new UE3ParticleEmitter(emitter, meshMaterial ?? material, mesh) { DynamicParameters = dynamicParameters };
    }

    private static Vector3 ToMetres(Vector3 value) => new Vector3(value.X, -value.Y, value.Z) * _metresPerUnit;

    private static ParticleFloat Scale(ParticleFloat value, float factor) => new(
        [.. value.Min.Select(key => key with { Value = key.Value * factor })],
        [.. value.Max.Select(key => key with { Value = key.Value * factor })]);

    private static ParticleVector? Map(ParticleVector? value, Func<Vector3, Vector3> map) => value is null ? null : new(
        [.. value.Min.Select(key => key with { Value = map(key.Value) })],
        [.. value.Max.Select(key => key with { Value = map(key.Value) })]);
}
