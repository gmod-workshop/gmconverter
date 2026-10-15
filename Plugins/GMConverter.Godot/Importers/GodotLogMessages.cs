using Microsoft.Extensions.Logging;

namespace GMConverter.Godot.Importers;

internal static partial class GodotLogMessages
{
    [LoggerMessage(
        EventId = 3100,
        Level = LogLevel.Warning,
        Message = "Skipped {Count} Godot textures that could not be read: {Names}")]
    public static partial void SkippedTextures(this ILogger logger, int count, string names);

    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Warning,
        Message = "Skipped {Count} Godot meshes Godot generates at run time: {Names}")]
    public static partial void SkippedMeshes(this ILogger logger, int count, string names);
}
