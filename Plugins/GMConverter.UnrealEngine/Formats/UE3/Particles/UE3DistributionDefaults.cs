using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.UE3.Particles;

/// <summary>
/// Values a cooked distribution leaves out because they equal its archetype's: the engine's default
/// sub-objects (<c>Engine.Default__&lt;Module&gt;.&lt;Distribution&gt;</c>), which live in the script
/// package <c>Engine.u</c>. Games may ship that encrypted (CrossFire HD does), so the defaults the
/// converted modules rely on are UE3's, kept here. Each agrees with the game data: across CrossFire
/// HD's ~25,000 spawn modules no rate is written as 20 and no rate scale as 1, start alpha is never
/// written as 1, start size never as (1,1,1), and orbits write zero where these are non-zero.
/// Anything not listed takes the distribution class's own default of zero.
/// </summary>
internal static class UE3DistributionDefaults
{
    private static readonly Dictionary<string, Dictionary<string, object>> _defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ParticleModuleSpawn.RequiredDistributionSpawnRate"] = new() { ["Constant"] = 20f },
        ["ParticleModuleSpawn.RequiredDistributionSpawnRateScale"] = new() { ["Constant"] = 1f },
        ["ParticleModuleColor.DistributionStartAlpha"] = new() { ["Constant"] = 1f },
        // Not settled by the data (white is also written explicitly); white leaves the particle colour
        // to the texture and the over-life colour.
        ["ParticleModuleColor.DistributionStartColor"] = new() { ["Constant"] = Vector3.One },
        ["ParticleModuleSize.DistributionStartSize"] = new() { ["Min"] = Vector3.One, ["Max"] = Vector3.One },
        ["ParticleModuleRotation.DistributionStartRotation"] = new() { ["Min"] = 0f, ["Max"] = 1f },
        ["ParticleModuleOrbit.DistributionOffsetAmount"] = new() { ["Min"] = Vector3.Zero, ["Max"] = new Vector3(0f, 50f, 0f) },
        ["ParticleModuleOrbit.DistributionRotationAmount"] = new() { ["Min"] = Vector3.Zero, ["Max"] = Vector3.One },
        ["ParticleModuleOrbit.DistributionRotationRateAmount"] = new() { ["Min"] = Vector3.Zero, ["Max"] = Vector3.One },
    };

    /// <summary>The default for <paramref name="property"/> of the distribution whose archetype path is <paramref name="archetype"/>.</summary>
    public static T? Find<T>(string? archetype, string property)
        where T : struct
    {
        if (archetype is null)
        {
            return null;
        }

        var key = archetype.StartsWith("Engine.Default__", StringComparison.Ordinal) ? archetype["Engine.Default__".Length..] : archetype;
        return _defaults.TryGetValue(key, out var values) && values.TryGetValue(property, out var value) && value is T typed ? typed : null;
    }
}
