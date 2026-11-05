using System.Text.Json;
using System.Text.Json.Serialization;

namespace GMConverter.UnrealEngine.Formats.PSA;

/// <summary>
/// Playback facts for an exported animation sequence, which PSA files cannot carry. The explorer
/// writes it next to the PSA and the PSK importer reads it back; it never leaves this plugin.
/// </summary>
/// <param name="RootMotion">The sequence's root bone motion moves the character (Unreal's bEnableRootMotion).</param>
/// <param name="Loops">The sequence is meant to repeat (Unreal's bLoop).</param>
/// <param name="Events">The sequence's notifies that exporters can use, such as footsteps and sounds.</param>
/// <param name="SequenceEvents">
/// Events by sequence name, for PSAs holding several sequences; a sequence listed here uses these
/// instead of <paramref name="Events"/>.
/// </param>
/// <param name="MeshTranslationBones">
/// Bones that keep the mesh's own bind-pose translation instead of the animation's, as UE3 AnimSets
/// with bAnimRotationOnly play, so the clips fit meshes of other proportions.
/// </param>
internal sealed record PSAClipSidecar(
    bool RootMotion,
    bool Loops,
    IReadOnlyList<PSAClipEvent>? Events = null,
    IReadOnlyList<string>? MeshTranslationBones = null,
    IReadOnlyDictionary<string, IReadOnlyList<PSAClipEvent>>? SequenceEvents = null)
{
    private const string _extension = ".clip.json";
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>True when the sidecar says anything a PSA doesn't already.</summary>
    [JsonIgnore]
    public bool HasData => RootMotion || Loops || Events is { Count: > 0 } || MeshTranslationBones is { Count: > 0 } || SequenceEvents is { Count: > 0 };

    /// <summary>The events for one sequence of the PSA, matching its name as Unreal does (ignoring case).</summary>
    public IReadOnlyList<PSAClipEvent>? EventsFor(string sequence) =>
        SequenceEvents?.FirstOrDefault(pair => pair.Key.Equals(sequence, StringComparison.OrdinalIgnoreCase)).Value ?? Events;

    public static PSAClipSidecar? TryRead(string psaPath)
    {
        var path = GetPath(psaPath);
        return File.Exists(path) ? JsonSerializer.Deserialize<PSAClipSidecar>(File.ReadAllText(path), _jsonOptions) : null;
    }

    public void Write(string psaPath)
    {
        File.WriteAllText(GetPath(psaPath), JsonSerializer.Serialize(this, _jsonOptions));
    }

    private static string GetPath(string psaPath) => Path.ChangeExtension(psaPath, _extension);
}
