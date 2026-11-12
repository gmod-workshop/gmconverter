using Microsoft.Extensions.Logging;

namespace GMConverter.Core.Pipeline;

internal static partial class PipelineLogMessages
{
    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Warning,
        Message = "{Asset}: {Warning}")]
    public static partial void ExportWarning(this ILogger logger, string asset, string warning);
}
