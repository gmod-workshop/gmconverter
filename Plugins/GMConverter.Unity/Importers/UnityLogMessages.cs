using Microsoft.Extensions.Logging;

namespace GMConverter.Unity.Importers;

internal static partial class UnityLogMessages
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Warning,
        Message = "Skipped {Count} Unity textures in formats that cannot be decoded: {Names}")]
    public static partial void SkippedTextures(this ILogger logger, int count, string names);
}
