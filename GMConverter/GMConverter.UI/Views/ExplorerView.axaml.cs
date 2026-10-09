using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using GMConverter.UI.ViewModels;

namespace GMConverter.UI.Views;

public partial class ExplorerView : UserControl
{
    public ExplorerView()
    {
        InitializeComponent();
        ConfigurePathDrop(ExplorerRootDirectoryBox, path =>
        {
            if (DataContext is ExplorerViewModel viewModel)
            {
                viewModel.ExplorerRootDirectory = path;
            }
        });
    }

    private static void ConfigurePathDrop(Control control, Action<string> applyPath)
    {
        DragDrop.SetAllowDrop(control, true);

        control.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });

        control.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var item = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
            var path = item?.Path.LocalPath;
            if (path is not null)
            {
                if (File.Exists(path))
                {
                    path = Path.GetDirectoryName(path);
                }

                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    applyPath(path);
                }
            }

            e.Handled = true;
        });
    }

    private void ExplorerTree_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not ExplorerViewModel viewModel ||
            !viewModel.PreviewExplorerSelectionCommand.CanExecute(null))
        {
            return;
        }

        viewModel.PreviewExplorerSelectionCommand.Execute(null);
        e.Handled = true;
    }

    private async void BrowseExplorerRootDirectory_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ExplorerViewModel viewModel &&
            await BrowseFolderAsync("Select asset root directory") is { } path)
        {
            viewModel.ExplorerRootDirectory = path;
        }
    }

    private async Task<string?> BrowseFolderAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return null;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }
}
