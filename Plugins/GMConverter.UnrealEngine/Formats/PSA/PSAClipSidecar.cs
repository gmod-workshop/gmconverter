using System.Text.Json;

namespace GMConverter.UnrealEngine.Formats.PSA;

/// <summary>
/// Playback facts for an exported animation sequence, which PSA files cannot carry. The explorer
/// writes it next to the PSA and the PSK importer reads it back; it never leaves this plugin.
/// </summary>
/// <param name="RootMotion">The sequence's root bone motion moves the character (Unreal's bEnableRootMotion).</param>
/// <param name="Loops">The sequence is meant to repeat (Unreal's bLoop).</param>
internal sealed record PSAClipSidecar(bool RootMotion, bool Loops)
{
    private const string _extension = ".clip.json";
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

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
