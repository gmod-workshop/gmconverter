using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using GMConverter.UI.ViewModels;

namespace GMConverter.UI.Views;

public partial class ConvertView : UserControl
{
    public ConvertView()
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

        ConfigurePathDrop(InputPathBox, DropPathKind.File, path =>
        {
            if (DataContext is ConvertViewModel viewModel)
            {
                ApplyInputPath(viewModel, path);
            }
        });

        ConfigurePathDrop(AnimationPathBox, DropPathKind.File, path =>
        {
            if (DataContext is ConvertViewModel viewModel)
            {
                viewModel.AnimationPath = path;
            }
        });

        ConfigurePathDrop(OutputPathBox, DropPathKind.Folder, path =>
        {
            if (DataContext is ConvertViewModel viewModel)
            {
                viewModel.OutputPath = path;
            }
        });

        ConfigurePathDrop(GameDirectoryBox, DropPathKind.Folder, path =>
        {
            if (DataContext is ConvertViewModel viewModel)
            {
                viewModel.GameDirectory = path;
            }
        });

        ConfigurePathDrop(EngineDirectoryBox, DropPathKind.Folder, path =>
        {
            if (DataContext is ConvertViewModel viewModel)
            {
                viewModel.EngineDirectory = path;
            }
        });

        ConfigurePathDrop(MaterialDirectoryBox, DropPathKind.Folder, path =>
        {
            if (DataContext is ConvertViewModel viewModel)
            {
                viewModel.MaterialDirectory = path;
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
            DropPathKind.Folder when Directory.Exists(path) => path,
            DropPathKind.Folder when File.Exists(path) => Path.GetDirectoryName(path),
            _ => null
        };
    }

    private static void ApplyInputPath(ConvertViewModel viewModel, string path)
    {
        viewModel.InputPath = path;

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var format = extension switch
        {
            ".opt" => "opt",
            ".mdl" => "mdl",
            ".psk" or ".pskx" => "psk",
            ".def" => "mow",
            _ => null
        };

        if (format is not null &&
            viewModel.InputFormats.FirstOrDefault(option => option.Value == format) is { } option)
        {
            viewModel.SelectedInputFormat = option;
        }
    }

    private async void BrowseInputPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ConvertViewModel viewModel &&
            await BrowseFileAsync("Select input model", GetInputFileTypes(viewModel.SelectedInputFormat.Value)) is { } path)
        {
            ApplyInputPath(viewModel, path);
        }
    }

    private async void BrowseAnimationPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ConvertViewModel viewModel &&
            await BrowseFileAsync("Select animation file", [new FilePickerFileType("PSA animation") { Patterns = ["*.psa"] }]) is { } path)
        {
            viewModel.AnimationPath = path;
        }
    }

    private async void BrowseConfigPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ConvertViewModel viewModel &&
            await BrowseFileAsync("Select config file", [new FilePickerFileType("GMConverter config") { Patterns = ["*.ini"] }]) is { } path)
        {
            viewModel.ConfigPath = path;
        }
    }

    private async void BrowseOutputPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ConvertViewModel viewModel &&
            await BrowseFolderAsync("Select output folder") is { } path)
        {
            viewModel.OutputPath = path;
        }
    }

    private async void BrowseGameDirectory_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ConvertViewModel viewModel &&
            await BrowseFolderAsync("Select Source game directory") is { } path)
        {
            viewModel.GameDirectory = path;
        }
    }

    private async void BrowseEngineDirectory_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ConvertViewModel viewModel &&
            await BrowseFolderAsync("Select Source engine directory") is { } path)
        {
            viewModel.EngineDirectory = path;
        }
    }

    private async void BrowseMaterialDirectory_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ConvertViewModel viewModel &&
            await BrowseFolderAsync("Select material search directory") is { } path)
        {
            viewModel.MaterialDirectory = path;
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

    private static IReadOnlyList<FilePickerFileType> GetInputFileTypes(string inputFormat)
    {
        return inputFormat switch
        {
            "opt" => [new FilePickerFileType("X-Wing Alliance OPT") { Patterns = ["*.opt"] }],
            "mdl" => [new FilePickerFileType("Source MDL") { Patterns = ["*.mdl"] }],
            "psk" => [new FilePickerFileType("Unreal PSK") { Patterns = ["*.psk", "*.pskx"] }],
            "mow" => [new FilePickerFileType("Men of War model") { Patterns = ["*.def", "*.mdl"] }],
            _ => [new FilePickerFileType("Supported models") { Patterns = ["*.opt", "*.mdl", "*.psk", "*.pskx", "*.def"] }]
        };
    }

    private enum DropPathKind
    {
        File,
        Folder
    }
}
