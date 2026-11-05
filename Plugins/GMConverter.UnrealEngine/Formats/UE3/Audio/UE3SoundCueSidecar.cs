using System.Text.Json;
using System.Text.Json.Serialization;
using GMConverter.SDK.Audio;

namespace GMConverter.UnrealEngine.Formats.UE3.Audio;

/// <summary>
/// A UE3 SoundCue the explorer exported: its layers, each with the audio files (written beside this
/// file) it picks from and how it plays. The UE3 sound cue importer reads it back; it never leaves
/// this plugin. Layers are named by <see cref="UE3SoundCue.LayerName"/>, which animation events use too.
/// </summary>
internal sealed record UE3SoundCueSidecar(string Name, SoundCategory Category, IReadOnlyList<UE3SoundCueSidecar.Layer> Layers)
{
    public const string Extension = ".ue3cue";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static UE3SoundCueSidecar Read(string path)
    {
        return JsonSerializer.Deserialize<UE3SoundCueSidecar>(File.ReadAllText(path), _jsonOptions)
            ?? throw new InvalidDataException($"Empty UE3 sound cue sidecar: {path}");
    }

    public void Write(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(this, _jsonOptions));
    }

    /// <summary>A layer's audio files (relative to the sidecar) with their weights, and how it plays.</summary>
    public sealed record Layer(
        IReadOnlyList<LayerFile> Files,
        float VolumeMin,
        float VolumeMax,
        float PitchMin,
        float PitchMax,
        float? AttenuationMeters,
        bool Loops);

    public sealed record LayerFile(string File, float Weight);
}
