using System.Globalization;
using System.Text.Json;

namespace GMConverter.UI.Services;

/// <summary>
/// One-time migration for settings files written before Source options were schema-driven. Those
/// files stored the Source exporter's options as top-level fields; this copies them into the
/// <c>mdl</c> exporter's saved options so existing users keep their values. The model path is not
/// carried over: it was derived per model, and the exporter now derives it when left blank.
/// </summary>
/// <remarks>Safe to delete once settings files from before this change no longer matter.</remarks>
internal static class LegacySourceSettings
{
    private const string _format = "mdl";

    // The CoACD values older versions shipped as defaults. Files that still hold all three were
    // never tuned by the user, so they take the current defaults instead.
    private const double _oldDefaultCoacdThreshold = 0.01;
    private const int _oldDefaultMaxConvexPieces = 32;
    private const int _oldDefaultMaxHullVertices = 32;

    public static UiSettings Migrate(UiSettings settings, JsonElement root)
    {
        Dictionary<string, object?> values = [];
        CopyString(root, "StudioMdlPath", "studioMdlPath", values);
        CopyString(root, "VtfCmdPath", "vtfCmdPath", values);
        CopyBool(root, "BuildMaterials", "buildMaterials", values);
        CopyBool(root, "DeduplicateTextures", "material:deduplicateTextures", values);
        if (TryGet(root, "MaxTextureSize", JsonValueKind.Number, out var size) && size.TryGetInt32(out var maxTextureSize))
        {
            values["material:maxTextureSize"] = maxTextureSize.ToString(CultureInfo.InvariantCulture);
        }
        CopyBool(root, "GeneratePhysics", "physics:enabled", values);
        CopyString(root, "PhysicsMode", "physics:mode", values);
        if (TryGet(root, "PhysicsMass", JsonValueKind.Number, out var mass))
        {
            values["physics:mass"] = (float)mass.GetDouble();
        }
        if (!HasOldCoacdDefaults(root))
        {
            if (TryGet(root, "CoacdThreshold", JsonValueKind.Number, out var threshold))
            {
                values["physics:coacdThreshold"] = (float)threshold.GetDouble();
            }
            CopyInt(root, "MaxConvexPieces", "physics:maxConvexPieces", values);
            CopyInt(root, "MaxHullVertices", "physics:maxHullVertices", values);
        }

        if (values.Count == 0)
        {
            return settings;
        }

        // Values already saved under the new scheme win over the legacy fields.
        var exporterOptions = settings.ExporterOptions is null
            ? new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, Dictionary<string, object?>>(settings.ExporterOptions, StringComparer.OrdinalIgnoreCase);
        var mdl = exporterOptions.TryGetValue(_format, out var existing)
            ? new Dictionary<string, object?>(existing)
            : [];
        foreach (var (key, value) in values)
        {
            mdl.TryAdd(key, value);
        }
        exporterOptions[_format] = mdl;
        return settings with { ExporterOptions = exporterOptions };
    }

    private static bool HasOldCoacdDefaults(JsonElement root)
    {
        return TryGet(root, "CoacdThreshold", JsonValueKind.Number, out var threshold) &&
            Math.Abs(threshold.GetDouble() - _oldDefaultCoacdThreshold) < 0.000001 &&
            TryGet(root, "MaxConvexPieces", JsonValueKind.Number, out var pieces) && pieces.TryGetInt32(out var maxPieces) &&
            maxPieces == _oldDefaultMaxConvexPieces &&
            TryGet(root, "MaxHullVertices", JsonValueKind.Number, out var vertices) && vertices.TryGetInt32(out var maxVertices) &&
            maxVertices == _oldDefaultMaxHullVertices;
    }

    private static void CopyString(JsonElement root, string legacyName, string key, Dictionary<string, object?> values)
    {
        if (TryGet(root, legacyName, JsonValueKind.String, out var value) && !string.IsNullOrWhiteSpace(value.GetString()))
        {
            values[key] = value.GetString();
        }
    }

    private static void CopyBool(JsonElement root, string legacyName, string key, Dictionary<string, object?> values)
    {
        if (root.TryGetProperty(legacyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            values[key] = value.GetBoolean();
        }
    }

    private static void CopyInt(JsonElement root, string legacyName, string key, Dictionary<string, object?> values)
    {
        if (TryGet(root, legacyName, JsonValueKind.Number, out var value) && value.TryGetInt32(out var number))
        {
            values[key] = number;
        }
    }

    private static bool TryGet(JsonElement root, string name, JsonValueKind kind, out JsonElement value)
    {
        return root.TryGetProperty(name, out value) && value.ValueKind == kind;
    }
}
