using System.Diagnostics;
using System.Globalization;

namespace GMConverter.Common;

// Lightweight Stopwatch-based instrumentation. Each process truncates the log on first use and
// appends timestamped BEGIN/END pairs from scopes opened via Measure(). Writes go to both a
// well-known file under the OS temp directory and (best-effort) the active console. The helper
// is intentionally side-effect-free on failure: any IO problem is swallowed so instrumentation
// never blocks a user-visible operation.
internal static class PerfTimer
{
    public static string LogPath { get; } =
        Path.Combine(Path.GetTempPath(), "GMConverter.Perf.log");

    private static readonly object _writeLock = new();
    private static readonly Stopwatch _processClock = Stopwatch.StartNew();

    static PerfTimer()
    {
        try
        {
            File.WriteAllText(
                LogPath,
                FormattableString.Invariant(
                    $"# GMConverter perf log | pid {Environment.ProcessId} | started {DateTimeOffset.Now:O}{Environment.NewLine}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Telemetry is best-effort; never propagate filesystem failures from instrumentation.
            // Anything outside this expected set (OOM, programmer error) propagates as normal so
            // unrelated bugs aren't silently masked by the perf log initializer.
        }
    }

    public static IDisposable Measure(string category, string operation, string? detail = null)
    {
        return new Scope(category, operation, detail);
    }

    public static void Log(string category, string message)
    {
        var elapsed = _processClock.Elapsed;
        var line = string.Format(
            CultureInfo.InvariantCulture,
            "[{0:hh\\:mm\\:ss\\.fff}] [{1,-10}] {2}",
            elapsed,
            category,
            message);

        lock (_writeLock)
        {
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort telemetry — filesystem failures here must never block the caller.
            }

            try
            {
                Console.WriteLine(line);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Console may be detached (windowed app, redirected stream closed). Drop the line.
            }
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _category;
        private readonly string _operation;
        private readonly string? _detail;
        private readonly long _startTimestamp;
        private bool _disposed;

        public Scope(string category, string operation, string? detail)
        {
            _category = category;
            _operation = operation;
            _detail = detail;
            _startTimestamp = Stopwatch.GetTimestamp();
            Log(category, detail is null ? $"BEGIN {operation}" : $"BEGIN {operation} :: {detail}");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            var elapsed = Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
            var suffix = _detail is null ? string.Empty : $" :: {_detail}";
            var message = string.Format(
                CultureInfo.InvariantCulture,
                "END   {0} ({1:F1} ms){2}",
                _operation,
                elapsed,
                suffix);
            Log(_category, message);
        }
    }
}
