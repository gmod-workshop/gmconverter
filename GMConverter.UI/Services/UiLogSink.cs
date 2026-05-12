using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace GMConverter.UI.Services;

internal sealed class UiLogSink
{
    public ObservableCollection<string> Lines { get; } = [];

    public void Append(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            Dispatcher.UIThread.Post(() => Lines.Add(line));
        }
    }

    public void Clear()
    {
        Dispatcher.UIThread.Post(Lines.Clear);
    }
}
