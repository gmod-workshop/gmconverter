using System.Globalization;
using Microsoft.Extensions.Logging;

namespace GMConverter.UI.Services;

/// <summary>
/// Minimal file-backed <see cref="ILoggerProvider"/> used during host startup before Avalonia
/// is ready (and therefore before <see cref="UiLogSink"/> can post entries to the UI thread).
/// Plugin discovery and load failures land here so they aren't silent if a plugin fails to load
/// at startup. The log path is exposed as a static property so the rest of the app can surface
/// it (e.g. in an "About" pane) when we eventually wire that up.
/// </summary>
internal sealed class PluginLogFileProvider : ILoggerProvider
{
    public static string LogPath { get; } =
        Path.Join(Path.GetTempPath(), "GMConverter.Plugins.log");

    private static readonly object _writeLock = new();

    static PluginLogFileProvider()
    {
        try
        {
            File.WriteAllText(
                LogPath,
                FormattableString.Invariant(
                    $"# GMConverter plugin log | pid {Environment.ProcessId} | started {DateTimeOffset.Now:O}{Environment.NewLine}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Telemetry is best-effort; never propagate filesystem failures from instrumentation.
            _ = ex;
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName);
    }

    public void Dispose()
    {
    }

    private sealed class FileLogger(string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var line = string.Format(
                CultureInfo.InvariantCulture,
                "[{0:O}] [{1,-11}] {2}: {3}",
                DateTimeOffset.Now,
                logLevel,
                categoryName,
                message);

            lock (_writeLock)
            {
                try
                {
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                    if (exception is not null)
                    {
                        File.AppendAllText(LogPath, exception.ToString() + Environment.NewLine);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best-effort telemetry — filesystem failures here must never block plugin load.
                    _ = ex;
                }
            }
        }
    }
}
