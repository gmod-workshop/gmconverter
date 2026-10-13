using System.Text.Json;
using System.Text.Json.Serialization;
using GMConverter.SDK.Audio;

namespace GMConverter.UnrealEngine.Formats.Wwise;

/// <summary>
/// A Wwise event the explorer exported: its name, the <c>.wem</c> media it plays (written beside
/// this file), and how it plays them. The Wwise event importer reads it back; it never leaves this
/// plugin.
/// </summary>
/// <param name="Name">The event's asset name, which animation notifies play it by.</param>
/// <param name="Category">What kind of sound it is.</param>
/// <param name="AttenuationMeters">How far away it can be heard, or null for a sound not placed in the world.</param>
/// <param name="Media">The media it picks from.</param>
internal sealed record WwiseEventSidecar(
    string Name,
    SoundCategory Category,
    float? AttenuationMeters,
    IReadOnlyList<WwiseEventSidecarMedia> Media)
{
    public const string Extension = ".akevent";

    /// <summary>
    /// The shape this build writes. The explorer re-exports events whose sidecar has another
    /// version, so cached sounds pick up new mixing without re-exporting every model.
    /// </summary>
    public const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public int Version { get; init; } = CurrentVersion;

    /// <summary>The event's volume in decibels.</summary>
    public float VolumeDb { get; init; }

    /// <summary>The lowest random offset added to <see cref="VolumeDb"/> each play, in decibels.</summary>
    public float VolumeMinDb { get; init; }

    /// <summary>The highest random offset added to <see cref="VolumeDb"/> each play, in decibels.</summary>
    public float VolumeMaxDb { get; init; }

    /// <summary>The event's pitch offset in cents.</summary>
    public float PitchCents { get; init; }

    /// <summary>The lowest random offset added to <see cref="PitchCents"/> each play.</summary>
    public float PitchMinCents { get; init; }

    /// <summary>The highest random offset added to <see cref="PitchCents"/> each play.</summary>
    public float PitchMaxCents { get; init; }

    public SoundSelection Selection { get; init; } = SoundSelection.Random;

    public bool Loops { get; init; }

    public static WwiseEventSidecar Read(string path)
    {
        return JsonSerializer.Deserialize<WwiseEventSidecar>(File.ReadAllText(path), _jsonOptions)
            ?? throw new InvalidDataException($"Empty Wwise event sidecar: {path}");
    }

    /// <summary>The version of the sidecar at <paramref name="path"/>, or null when it cannot be read.</summary>
    public static int? TryReadVersion(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty(nameof(Version), out var version) && version.TryGetInt32(out var value) ? value : 1;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Write(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(this, _jsonOptions));
    }
}
