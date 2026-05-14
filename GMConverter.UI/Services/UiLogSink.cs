using System.Collections.ObjectModel;
using Avalonia.Threading;
using GMConverter.UI.Models;
using Microsoft.Extensions.Logging;

namespace GMConverter.UI.Services;

internal sealed class UiLogSink
{
    private const int _maxEntries = 2000;

    public ObservableCollection<UiLogEntry> Entries { get; } = [];

    public ObservableCollection<string> Lines { get; } = [];

    public void Append(string? line)
    {
        Append("Info", line);
    }

    public void Append(LogLevel logLevel, string? line)
    {
        Append(logLevel.ToString(), line);
    }

    public void Append(string level, string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            var timestamp = DateTimeOffset.Now;
            var entries = line
                .ReplaceLineEndings("\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(message => new UiLogEntry(timestamp, level, message))
                .ToArray();

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var entry in entries)
                {
                    Entries.Add(entry);
                    Lines.Add(entry.Line);
                }

                while (Entries.Count > _maxEntries)
                {
                    Entries.RemoveAt(0);
                }

                while (Lines.Count > _maxEntries)
                {
                    Lines.RemoveAt(0);
                }
            });
        }
    }

    public void Clear()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Entries.Clear();
            Lines.Clear();
        });
    }
}
