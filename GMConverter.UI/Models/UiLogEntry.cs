using System.Globalization;

namespace GMConverter.UI.Models;

public sealed class UiLogEntry(DateTimeOffset timestamp, string level, string message)
{
    public DateTimeOffset Timestamp { get; } = timestamp;

    public string Level { get; } = level;

    public string Message { get; } = message;

    public string TimestampText => Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    public string Line => $"[{TimestampText}] {Level}: {Message}";
}
