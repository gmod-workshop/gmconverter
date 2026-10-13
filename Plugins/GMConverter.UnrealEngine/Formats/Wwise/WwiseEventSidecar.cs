using System.Text.Json;
using System.Text.Json.Serialization;
using GMConverter.SDK.Audio;

namespace GMConverter.UnrealEngine.Formats.Wwise;

/// <summary>
/// A Wwise event the explorer exported: its name, the <c>.wem</c> media it plays (written beside
/// this file), and what is known about how it plays. The Wwise event importer reads it back; it
/// never leaves this plugin.
/// </summary>
/// <param name="Name">The event's asset name, which animation notifies play it by.</param>
/// <param name="Category">What kind of sound it is.</param>
/// <param name="AttenuationMeters">How far away it can be heard, or null for a sound not placed in the world.</param>
/// <param name="Media">The <c>.wem</c> files it picks from, relative to this file.</param>
internal sealed record WwiseEventSidecar(
    string Name,
    SoundCategory Category,
    float? AttenuationMeters,
    IReadOnlyList<string> Media)
{
    public const string Extension = ".akevent";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static WwiseEventSidecar Read(string path)
    {
        return JsonSerializer.Deserialize<WwiseEventSidecar>(File.ReadAllText(path), _jsonOptions)
            ?? throw new InvalidDataException($"Empty Wwise event sidecar: {path}");
    }

    public void Write(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(this, _jsonOptions));
    }
}
