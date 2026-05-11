using Microsoft.Extensions.Logging;

namespace GMConverter.Formats.MOW;

internal static partial class MOWLogMessages
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Warning,
        Message = "Men of War animation file not found for sequence '{SequenceName}': {AnimationPath}")]
    public static partial void MissingAnimationFile(this ILogger logger, string sequenceName, string animationPath);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Men of War material file not found: {MaterialPath}")]
    public static partial void MissingMaterialFile(this ILogger logger, string materialPath);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Men of War texture file not found for reference '{TextureName}'")]
    public static partial void MissingTextureReference(this ILogger logger, string textureName);
}
