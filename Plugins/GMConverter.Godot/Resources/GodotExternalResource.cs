namespace GMConverter.Godot.Resources;

/// <summary>A resource kept in a file of its own, which a resource file refers to by path.</summary>
/// <param name="Type">The resource's class, such as <c>Texture2D</c>.</param>
/// <param name="Path">Its resource path, such as <c>res://textures/wood.png</c>.</param>
/// <param name="Uid">
/// Its unique id, or -1 when the file saved none. Godot finds a resource by its id when the path
/// no longer names a file, such as after the source file was renamed.
/// </param>
internal sealed record GodotExternalResource(string Type, string Path, long Uid = -1);
