using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Input;
using GMConverter.UI.Models;
using GMConverter.UI.Services;

namespace GMConverter.UI.ViewModels;

public sealed partial class ConsoleViewModel : ViewModelBase, IDisposable
{
    private readonly UiLogSink _logSink;
    private bool _disposed;

    internal ConsoleViewModel(UiLogSink logSink)
    {
        _logSink = logSink;
        Entries.CollectionChanged += Entries_CollectionChanged;
    }

    public ObservableCollection<UiLogEntry> Entries => _logSink.Entries;

    public bool HasEntries => Entries.Count > 0;

    public bool HasNoEntries => !HasEntries;

    public string ConsoleSummary => Entries.Count == 1 ? "1 log entry" : $"{Entries.Count} log entries";

    public string AllText => string.Join(Environment.NewLine, Entries.Select(entry => entry.Line));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Entries.CollectionChanged -= Entries_CollectionChanged;
    }

    [RelayCommand(CanExecute = nameof(CanClearLogs))]
    private void ClearLogs()
    {
        _logSink.Clear();
    }

    private bool CanClearLogs()
    {
        return HasEntries;
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasNoEntries));
        OnPropertyChanged(nameof(ConsoleSummary));
        OnPropertyChanged(nameof(AllText));
        ClearLogsCommand.NotifyCanExecuteChanged();
    }
}
