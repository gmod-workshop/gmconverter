using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using GMConverter.UI.ViewModels;

namespace GMConverter.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        InitializeDragDrop();
    }

    private void InitializeDragDrop()
    {
        ConfigurePathDrop(ConfigPathBox, DropPathKind.File, path =>
        {
            if (DataContext is ConvertViewModel viewModel)
            {
                viewModel.ConfigPath = path;
            }
        });
    }

    private static void ConfigurePathDrop(Control control, DropPathKind pathKind, Action<string> applyPath)
    {
        DragDrop.SetAllowDrop(control, true);

        control.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });

        control.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var path = GetDroppedPath(e, pathKind);
            if (path is not null)
            {
                applyPath(path);
            }

            e.Handled = true;
        });
    }

    private static string? GetDroppedPath(DragEventArgs e, DropPathKind pathKind)
    {
        var item = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
        if (item is null)
        {
            return null;
        }

        var path = item.Path.LocalPath;
        return pathKind switch
        {
            DropPathKind.File when File.Exists(path) => path,
            _ => null
        };
    }

    private async void BrowseConfigPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ConvertViewModel viewModel &&
            await BrowseFileAsync("Select config file", [new FilePickerFileType("GMConverter config") { Patterns = ["*.ini"] }]) is { } path)
        {
            viewModel.ConfigPath = path;
        }
    }

    private async Task<string?> BrowseFileAsync(string title, IReadOnlyList<FilePickerFileType>? filters = null)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return null;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters
        });

        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    private enum DropPathKind
    {
        File
    }
}
