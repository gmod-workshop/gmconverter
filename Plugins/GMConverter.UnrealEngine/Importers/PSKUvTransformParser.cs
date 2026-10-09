using System.Globalization;
using System.Numerics;
using GMConverter.SDK.Materials;

namespace GMConverter.UnrealEngine.Importers;

// Reads the UvTransform / DetailUvTransform sidecar format written by UnrealMaterialExporter:
// ";"-separated "name=values" segments where the first segment of each name wins.
internal static class PSKUvTransformParser
{
    public static MaterialUvTransform? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Dictionary<string, float[]> segments = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> kinds = new(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0 || segments.ContainsKey(segment[..separator]))
            {
                continue;
            }

            var name = segment[..separator];
            var parts = segment[(separator + 1)..].Split(',', StringSplitOptions.TrimEntries);
            if (name is "u" or "v" && parts.Length > 0)
            {
                kinds[name] = parts[0];
                parts = parts[1..];
            }

            var numbers = new float[parts.Length];
            if (parts.Select((part, index) => float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[index])).All(parsed => parsed))
            {
                segments[name] = numbers;
            }
        }

        var transform = new MaterialUvTransform
        {
            Scale = Vector(segments, "scale") ?? Vector2.One,
            Center = Vector(segments, "center") ?? Vector2.Zero,
            RotationDegrees = Scalar(segments, "rotation"),
            RotationRateDegrees = Scalar(segments, "rotationrate"),
            ScrollRate = Vector(segments, "scroll") ?? Vector2.Zero,
            U = Oscillation(segments, kinds, "u"),
            V = Oscillation(segments, kinds, "v"),
        };
        return transform.IsIdentity ? null : transform;
    }

    public static MaterialLayerBlend? ParseBlend(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "ADD" => MaterialLayerBlend.Add,
            "MULTIPLY" => MaterialLayerBlend.Multiply,
            "MULTIPLY2X" => MaterialLayerBlend.Multiply2X,
            "MULTIPLY4X" => MaterialLayerBlend.Multiply4X,
            _ => null
        };
    }

    private static Vector2? Vector(Dictionary<string, float[]> segments, string name)
    {
        return segments.TryGetValue(name, out var values) && values.Length == 2 ? new Vector2(values[0], values[1]) : null;
    }

    private static float Scalar(Dictionary<string, float[]> segments, string name)
    {
        return segments.TryGetValue(name, out var values) && values.Length == 1 ? values[0] : 0f;
    }

    private static MaterialOscillation? Oscillation(Dictionary<string, float[]> segments, Dictionary<string, string> kinds, string axis)
    {
        if (!segments.TryGetValue(axis, out var values) || values.Length != 3 || !kinds.TryGetValue(axis, out var kindName))
        {
            return null;
        }

        MaterialOscillationKind? kind = kindName.ToUpperInvariant() switch
        {
            "PAN" => MaterialOscillationKind.Pan,
            "STRETCH" => MaterialOscillationKind.Stretch,
            "JITTER" => MaterialOscillationKind.Jitter,
            _ => null
        };
        return kind is { } known ? new MaterialOscillation(known, values[0], values[1], values[2]) : null;
    }
}
