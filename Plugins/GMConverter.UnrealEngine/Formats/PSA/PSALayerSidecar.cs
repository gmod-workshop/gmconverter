using System.Text.Json;
using System.Text.Json.Serialization;
using GMConverter.SDK.Animation;

namespace GMConverter.UnrealEngine.Formats.PSA;

/// <summary>
/// Layering facts for an exported additive animation, which PSA files cannot carry. The explorer
/// writes it next to the PSA and the PSK importer reads it back; it never leaves this plugin.
/// </summary>
/// <param name="Space">The space the layer is applied in.</param>
/// <param name="BasePath">The PSA holding the base pose, relative to this sidecar, or null for the bind pose.</param>
/// <param name="BaseFrame">The frame of <paramref name="BasePath"/> that is the base pose.</param>
/// <param name="BaseId">
/// The base animation's game path. Every additive is exported into its own folder with its own copy
/// of the base, so this is what tells clips that share a base apart from clips that don't.
/// </param>
internal sealed record PSALayerSidecar(
    [property: JsonConverter(typeof(JsonStringEnumConverter<LayerSpace>))] LayerSpace Space,
    string? BasePath,
    int BaseFrame,
    string? BaseId = null)
{
    private const string _extension = ".layer.json";
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public static string GetPath(string psaPath) => Path.ChangeExtension(psaPath, _extension);

    public static PSALayerSidecar? TryRead(string psaPath)
    {
        var path = GetPath(psaPath);
        return File.Exists(path) ? JsonSerializer.Deserialize<PSALayerSidecar>(File.ReadAllText(path), _jsonOptions) : null;
    }

    public void Write(string psaPath)
    {
        File.WriteAllText(GetPath(psaPath), JsonSerializer.Serialize(this, _jsonOptions));
    }

    /// <summary>The full path of the base pose PSA, or null when the base is the bind pose.</summary>
    public string? ResolveBasePath(string psaPath)
    {
        return BasePath is null
            ? null
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(psaPath) ?? string.Empty, BasePath));
    }
}
