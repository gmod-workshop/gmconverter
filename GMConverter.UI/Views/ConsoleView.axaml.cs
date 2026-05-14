using Avalonia.Controls;
using Avalonia.Input.Platform;
using GMConverter.UI.ViewModels;

namespace GMConverter.UI.Views;

public partial class ConsoleView : UserControl
{
    public ConsoleView()
    {
        InitializeComponent();
    }

    private async void CopyLogs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ConsoleViewModel viewModel ||
            string.IsNullOrWhiteSpace(viewModel.AllText) ||
            TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(viewModel.AllText);
    }
}
