using Microsoft.Extensions.Logging;

namespace GMConverter.UI.Services;

internal sealed class UiLoggerProvider(UiLogSink logSink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new UiLogger(categoryName, logSink);
    }

    public void Dispose()
    {
    }

    private sealed class UiLogger(string categoryName, UiLogSink logSink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Warning;
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
            if (!string.IsNullOrWhiteSpace(message))
            {
                logSink.Append($"{logLevel}: {categoryName}: {message}");
            }

            if (exception is not null)
            {
                logSink.Append(exception.Message);
            }
        }
    }
}
