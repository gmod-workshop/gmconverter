using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using GMConverter.UI.ViewModels;

namespace GMConverter.UI.Controls.Settings;

public partial class SourceEngineSettings : UserControl
{
    public SourceEngineSettings()
    {
        InitializeComponent();
        InitializeDragDrop();
    }

    private void InitializeDragDrop()
    {
        ConfigurePathDrop(StudioMdlPathBox, path =>
        {
            if (DataContext is ConvertViewModel viewModel)
            {
                viewModel.StudioMdlPath = path;
            }
        });

        ConfigurePathDrop(VtfCmdPathBox, path =>
        {
            if (DataContext is ConvertViewModel viewModel)
            {
                viewModel.VtfCmdPath = path;
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
            if (item is not null)
            {
                var path = item.Path.LocalPath;
                if (File.Exists(path))
                {
                    applyPath(path);
                }
            }

            e.Handled = true;
        });
    }

    private async void BrowseStudioMdlPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ConvertViewModel viewModel &&
            await BrowseFileAsync("Select StudioMDL executable", [new FilePickerFileType("StudioMDL") { Patterns = ["*.exe"] }]) is { } path)
        {
            viewModel.StudioMdlPath = path;
        }
    }

    private async void BrowseVtfCmdPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ConvertViewModel viewModel &&
            await BrowseFileAsync("Select VTFCmd executable", [new FilePickerFileType("VTFCmd") { Patterns = ["*.exe"] }]) is { } path)
        {
            viewModel.VtfCmdPath = path;
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
}
