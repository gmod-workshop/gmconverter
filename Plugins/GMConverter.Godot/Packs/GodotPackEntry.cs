namespace GMConverter.Godot.Packs;

/// <summary>A file in a Godot pack.</summary>
/// <param name="Path">The file's resource path, such as <c>res://models/table.glb.import</c>.</param>
/// <param name="Offset">Where the file starts in the pack's host file.</param>
/// <param name="Size">The file's length in bytes.</param>
internal sealed record GodotPackEntry(string Path, long Offset, long Size);
