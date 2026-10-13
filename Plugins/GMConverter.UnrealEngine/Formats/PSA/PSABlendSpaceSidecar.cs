using System.Text.Json;

namespace GMConverter.UnrealEngine.Formats.PSA;

/// <summary>
/// An Unreal blend space or aim offset, which PSA files cannot describe: its axes and which
/// exported sequences sit where. The explorer writes it beside the sample PSAs it exports, and the
/// PSK importer reads it back; it never leaves this plugin.
/// </summary>
/// <param name="Name">The blend space's asset name.</param>
/// <param name="IsAimOffset">True for an aim offset, whose axes are aim yaw and pitch.</param>
/// <param name="Axes">Its axes, one or two.</param>
/// <param name="Samples">The sequences and their positions.</param>
internal sealed record PSABlendSpaceSidecar(
    string Name,
    bool IsAimOffset,
    IReadOnlyList<PSABlendSpaceAxis> Axes,
    IReadOnlyList<PSABlendSpaceSample> Samples)
{
    public const string Extension = ".blendspace.json";
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public static PSABlendSpaceSidecar Read(string path)
    {
        return JsonSerializer.Deserialize<PSABlendSpaceSidecar>(File.ReadAllText(path), _jsonOptions)
            ?? throw new InvalidDataException($"Empty blend space sidecar: {path}");
    }

    public void Write(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(this, _jsonOptions));
    }
}
