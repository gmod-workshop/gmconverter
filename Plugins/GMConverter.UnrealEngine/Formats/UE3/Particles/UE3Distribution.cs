using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.SDK.Particles;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Particles;

/// <summary>
/// Reads a Cascade module's <c>RawDistributionFloat</c>/<c>RawDistributionVector</c>. Cooking bakes
/// each into a lookup table (the output range, then evenly spaced samples of one value or a min/max
/// pair, from <c>LookupTableStartTime</c> every 1/<c>LookupTableTimeScale</c>), which also captures
/// cubic curves; without a table the distribution object itself is read (constants, uniform ranges and
/// curves, taken linearly).
/// </summary>
internal static class UE3Distribution
{
    public static ParticleFloat? ReadFloat(UE3Package package, UE3Properties module, string name)
    {
        if (module.GetStruct(name, package.Summary.FileVersion) is not { } raw)
        {
            return null;
        }

        if (Table(raw) is { } table)
        {
            return FloatFromTable(table, Layout(raw, 1));
        }

        if (Distribution(package, raw) is not { } distribution)
        {
            return null;
        }

        var properties = distribution.Properties;
        float Value(string property) => properties.GetFloat(property) ?? UE3DistributionDefaults.Find<float>(distribution.Archetype, property) ?? 0f;
        return distribution.Class switch
        {
            "DistributionFloatConstant" => ParticleFloat.Constant(Value("Constant")),
            "DistributionFloatUniform" => ParticleFloat.Range(Value("Min"), Value("Max")),
            "DistributionFloatConstantCurve" => ParticleFloat.Curve(
                [.. Points(package, properties).Select(point => new ParticleKey(point.GetFloat("InVal") ?? 0f, point.GetFloat("OutVal") ?? 0f))]),
            "DistributionFloatUniformCurve" => FloatRangeCurve(package, properties),
            _ => null,
        };
    }

    /// <summary>A vector distribution in Unreal's space and units; <paramref name="convert"/> moves each value into the SDK's.</summary>
    public static ParticleVector? ReadVector(UE3Package package, UE3Properties module, string name, Func<Vector3, Vector3> convert)
    {
        if (module.GetStruct(name, package.Summary.FileVersion) is not { } raw)
        {
            return null;
        }

        if (Table(raw) is { } table)
        {
            return VectorFromTable(table, Layout(raw, 3), convert);
        }

        if (Distribution(package, raw) is not { } distribution)
        {
            return null;
        }

        var vectors = distribution.Properties;
        Vector3 Vector(string property) => vectors.GetVector(property) ?? UE3DistributionDefaults.Find<Vector3>(distribution.Archetype, property) ?? Vector3.Zero;
        return distribution.Class switch
        {
            "DistributionVectorConstant" => ParticleVector.Constant(convert(Vector("Constant"))),
            "DistributionVectorUniform" => Ordered(
                [new ParticleVectorKey(0f, convert(Vector("Min")))],
                [new ParticleVectorKey(0f, convert(Vector("Max")))]),
            "DistributionVectorConstantCurve" => ParticleVector.Curve(
                [.. Points(package, vectors).Select(point => new ParticleVectorKey(point.GetFloat("InVal") ?? 0f, convert(point.GetVector("OutVal") ?? Vector3.Zero)))]),
            _ => null,
        };
    }

    /// <summary>A float lookup table as a distribution: one value per sample, or a min/max pair when random.</summary>
    public static ParticleFloat FloatFromTable(float[] table, UE3DistributionTable layout)
    {
        var keys = Samples(table, layout);
        return new ParticleFloat(
            [.. keys.Select(key => new ParticleKey(key.Time, table[key.Offset]))],
            [.. keys.Select(key => new ParticleKey(key.Time, table[key.Offset + (layout.Random ? 1 : 0)]))]);
    }

    /// <summary>A vector lookup table as a distribution: one vector per sample, or a min/max pair when random.</summary>
    public static ParticleVector VectorFromTable(float[] table, UE3DistributionTable layout, Func<Vector3, Vector3> convert)
    {
        var keys = Samples(table, layout);
        Vector3 At(int offset) => convert(new Vector3(table[offset], table[offset + 1], table[offset + 2]));
        return Ordered(
            [.. keys.Select(key => new ParticleVectorKey(key.Time, At(key.Offset)))],
            [.. keys.Select(key => new ParticleVectorKey(key.Time, At(key.Offset + (layout.Random ? 3 : 0))))]);
    }

    private static UE3DistributionTable Layout(UE3Properties raw, int width) => new(
        (raw.GetByte("LookupTableNumElements") ?? 1) >= 2,
        Math.Max(width, (int)(raw.GetByte("LookupTableChunkSize") ?? (byte)width)),
        raw.GetFloat("LookupTableStartTime") ?? 0f,
        raw.GetFloat("LookupTableTimeScale") ?? 0f);

    // Mirroring (Unreal's Y flips into the SDK's) can swap a range's ends; keep min below max per axis.
    private static ParticleVector Ordered(IReadOnlyList<ParticleVectorKey> min, IReadOnlyList<ParticleVectorKey> max) => new(
        [.. min.Zip(max, (low, high) => low with { Value = Vector3.Min(low.Value, high.Value) })],
        [.. min.Zip(max, (low, high) => high with { Value = Vector3.Max(low.Value, high.Value) })]);

    private static float[]? Table(UE3Properties raw)
    {
        if (raw.GetArray("LookupTable") is not { Count: > 2 } array)
        {
            return null;
        }

        return [.. Enumerable.Range(0, array.Count).Select(_ => array.Elements.ReadSingle())];
    }

    // Each sample's time and offset in the table; the first two entries are the output range.
    private static List<(float Time, int Offset)> Samples(float[] table, UE3DistributionTable layout)
    {
        // Without a time scale every sample is the same value at the start.
        var count = layout.TimeScale > 0f ? (table.Length - 2) / layout.Chunk : Math.Min(1, (table.Length - 2) / layout.Chunk);
        return [.. Enumerable.Range(0, count).Select(index =>
            (layout.TimeScale > 0f ? layout.StartTime + (index / layout.TimeScale) : layout.StartTime, 2 + (index * layout.Chunk)))];
    }

    private static (string Class, UE3Properties Properties, string? Archetype)? Distribution(UE3Package package, UE3Properties raw)
    {
        if (package.GetExport(raw.GetObject("Distribution")) is not { } export)
        {
            return null;
        }

        try
        {
            var archetype = export.ArchetypeIndex == 0 ? null : package.GetPath(export.ArchetypeIndex);
            return (package.GetClassName(export), package.OpenComponent(export).Properties, archetype);
        }
        catch (GMConverterException)
        {
            return null;
        }
    }

    private static IReadOnlyList<UE3Properties> Points(UE3Package package, UE3Properties distribution) =>
        distribution.GetStruct("ConstantCurve", package.Summary.FileVersion)?.GetStructArray("Points", package.Summary.FileVersion) ?? [];

    // A uniform curve's points hold a Vector2D: X is the low curve, Y the high.
    private static ParticleFloat FloatRangeCurve(UE3Package package, UE3Properties distribution)
    {
        var points = Points(package, distribution);
        float Time(UE3Properties point) => point.GetFloat("InVal") ?? 0f;
        Vector2 Range(UE3Properties point) => point.GetVector2("OutVal") ?? Vector2.Zero;
        return new ParticleFloat(
            [.. points.Select(point => new ParticleKey(Time(point), Range(point).X))],
            [.. points.Select(point => new ParticleKey(Time(point), Range(point).Y))]);
    }
}
