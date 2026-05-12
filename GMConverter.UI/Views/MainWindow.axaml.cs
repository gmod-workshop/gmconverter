using System.ComponentModel;
using System.Numerics;
using Ab4d.SharpEngine.AvaloniaUI;
using Ab4d.SharpEngine.Cameras;
using Ab4d.SharpEngine.Common;
using Ab4d.SharpEngine.glTF;
using Ab4d.SharpEngine.Lights;
using Ab4d.SharpEngine.Materials;
using Ab4d.SharpEngine.SceneNodes;
using Ab4d.SharpEngine.Utilities;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GMConverter.UI.ViewModels;

namespace GMConverter.UI.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? subscribedViewModel;
    private PointerCameraController? previewCameraController;
    private TargetPositionCamera? previewCamera;
    private GroupNode? previewModelNodes;
    private GroupNode? previewPhysicsNodes;
    private MultiLineNode? previewWireframeNode;
    private MultiLineNode? previewPhysicsWireframeNode;
    private bool previewWireframeEnabled;
    private bool previewPhysicsEnabled;

    public MainWindow()
    {
        InitializeComponent();
        InitializePreviewScene();
        InitializeDragDrop();

        Unloaded += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SaveSettingsNow();
            }

            PreviewSceneView.Dispose();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnDataContextChanged(e);

        subscribedViewModel = DataContext as MainWindowViewModel;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyPreviewSettings(subscribedViewModel);
        }
    }

    private void InitializePreviewScene()
    {
        previewCamera = new TargetPositionCamera
        {
            Heading = -35,
            Attitude = -25,
            Distance = 200,
            TargetPosition = Vector3.Zero,
            ShowCameraLight = ShowCameraLightType.Always
        };

        PreviewSceneView.SceneView.Camera = previewCamera;
        PreviewSceneView.Scene.SetAmbientLight(0.45f);
        PreviewSceneView.Scene.Lights.Add(new DirectionalLight(new Vector3(-0.45f, -0.75f, -0.35f)));
        PreviewSceneView.Scene.Lights.Add(new DirectionalLight(new Vector3(0.55f, -0.35f, 0.65f)));
        previewCamera.CameraChanged += (_, _) => UpdatePreviewGizmo();
        PreviewGizmo.ViewRequested += PreviewGizmo_ViewRequested;
        UpdatePreviewGizmo();

        previewCameraController = new PointerCameraController(PreviewSceneView)
        {
            RotateCameraConditions = PointerAndKeyboardConditions.LeftPointerButtonPressed,
            MoveCameraConditions = PointerAndKeyboardConditions.LeftPointerButtonPressed | PointerAndKeyboardConditions.ControlKey,
            QuickZoomConditions = PointerAndKeyboardConditions.LeftPointerButtonPressed | PointerAndKeyboardConditions.RightPointerButtonPressed,
            ZoomMode = CameraZoomMode.PointerPosition,
            RotateAroundPointerPosition = true,
            CameraSmoothing = CameraController.CameraSmoothingPresets.Normal
        };
    }

    private void InitializeDragDrop()
    {
        ConfigurePathDrop(ConfigPathBox, DropPathKind.File, path =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.ConfigPath = path;
            }
        });

        ConfigurePathDrop(InputPathBox, DropPathKind.File, path =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                ApplyInputPath(viewModel, path);
            }
        });

        ConfigurePathDrop(AnimationPathBox, DropPathKind.File, path =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.AnimationPath = path;
            }
        });

        ConfigurePathDrop(OutputPathBox, DropPathKind.Folder, path =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.OutputPath = path;
            }
        });

        ConfigurePathDrop(GameDirectoryBox, DropPathKind.Folder, path =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.GameDirectory = path;
            }
        });

        ConfigurePathDrop(EngineDirectoryBox, DropPathKind.Folder, path =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.EngineDirectory = path;
            }
        });

        ConfigurePathDrop(MaterialDirectoryBox, DropPathKind.Folder, path =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.MaterialDirectory = path;
            }
        });

        ConfigurePathDrop(ExplorerRootDirectoryBox, DropPathKind.Folder, path =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.ExplorerRootDirectory = path;
            }
        });
    }

    private void ConfigurePathDrop(Control control, DropPathKind pathKind, Action<string> applyPath)
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

    private static void ApplyInputPath(MainWindowViewModel viewModel, string path)
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

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.PreviewModelPath) &&
            sender is MainWindowViewModel viewModel &&
            !string.IsNullOrWhiteSpace(viewModel.PreviewModelPath))
        {
            try
            {
                await LoadPreviewModelAsync(
                    viewModel.PreviewModelPath,
                    string.IsNullOrWhiteSpace(viewModel.PreviewPhysicsModelPath) ? null : viewModel.PreviewPhysicsModelPath);
            }
            catch (Exception ex)
            {
                SetStatus("Preview render failed.");
                AppendLog($"Preview render failed. {ex.Message}");
            }
        }
    }

    private async Task LoadPreviewModelAsync(string modelPath, string? physicsModelPath)
    {
        if (!File.Exists(modelPath))
        {
            SetStatus("Preview render failed.");
            AppendLog($"Preview render failed. File not found: {modelPath}");
            return;
        }

        PreviewSceneView.Scene.RootNode.Clear();
        previewModelNodes = null;
        previewPhysicsNodes = null;
        previewWireframeNode = null;
        previewPhysicsWireframeNode = null;
        previewWireframeEnabled = DataContext is MainWindowViewModel viewModel && viewModel.PreviewWireframe;
        previewPhysicsEnabled = false;

        var modelNodes = await ImportPreviewNodesAsync(modelPath);
        if (modelNodes is null)
        {
            return;
        }

        previewModelNodes = modelNodes;
        PreviewSceneView.Scene.RootNode.Add(modelNodes);

        if (modelNodes.WorldBoundingBox.IsUndefined)
        {
            modelNodes.Update();
        }

        if (!string.IsNullOrWhiteSpace(physicsModelPath) && File.Exists(physicsModelPath))
        {
            previewPhysicsNodes = await ImportPreviewNodesAsync(physicsModelPath);
            if (previewPhysicsNodes is not null)
            {
                previewPhysicsNodes.SetVisibility(false);
                PreviewSceneView.Scene.RootNode.Add(previewPhysicsNodes);
                if (previewPhysicsNodes.WorldBoundingBox.IsUndefined)
                {
                    previewPhysicsNodes.Update();
                }
            }
        }

        previewPhysicsEnabled = DataContext is MainWindowViewModel currentViewModel &&
            currentViewModel.PreviewPhysicsOverlay &&
            previewPhysicsNodes is not null;

        UpdatePreviewWireframe();
        UpdatePreviewPhysicsWireframe();
        SetPreviewCamera(heading: -35, attitude: -25, fit: true);

        RenderPreviewScene();
        SetStatus("Preview render loaded.");
        AppendLog("Preview render loaded.");
    }

    private async Task<GroupNode?> ImportPreviewNodesAsync(string modelPath)
    {
        var importer = new glTFImporter(null, PreviewSceneView.Scene.GpuDevice)
        {
            UsePbrMaterial = true,
            LoggerCallback = (_, message) =>
            {
                AppendLog(message);
            }
        };

        return await importer.ImportAsync(modelPath);
    }

    private void PreviewFit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        FitPreviewToModel();
    }

    private void ExplorerTree_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            !viewModel.PreviewExplorerSelectionCommand.CanExecute(null))
        {
            return;
        }

        viewModel.PreviewExplorerSelectionCommand.Execute(null);
        e.Handled = true;
    }

    private void PreviewProjection_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (previewCamera is null)
        {
            return;
        }

        var useOrthographic = PreviewProjectionToggle.IsChecked is true;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PreviewOrthographic = useOrthographic;
        }

        previewCamera.ProjectionType = useOrthographic
            ? ProjectionTypes.Orthographic
            : ProjectionTypes.Perspective;
        FitPreviewToModel();
    }

    private void PreviewWireframe_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        previewWireframeEnabled = PreviewWireframeToggle.IsChecked is true;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PreviewWireframe = previewWireframeEnabled;
        }

        UpdatePreviewWireframe();
        RenderPreviewScene();
    }

    private void PreviewPhysics_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var showPhysics = PreviewPhysicsToggle.IsChecked is true;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PreviewPhysicsOverlay = showPhysics;
        }

        previewPhysicsEnabled = showPhysics && previewPhysicsNodes is not null;
        UpdatePreviewPhysicsWireframe();
        RenderPreviewScene();
    }

    private void ApplyPreviewSettings(MainWindowViewModel viewModel)
    {
        PreviewProjectionToggle.IsChecked = viewModel.PreviewOrthographic;
        PreviewWireframeToggle.IsChecked = viewModel.PreviewWireframe;
        PreviewPhysicsToggle.IsChecked = viewModel.PreviewPhysicsOverlay;

        if (previewCamera is not null)
        {
            previewCamera.ProjectionType = viewModel.PreviewOrthographic
                ? ProjectionTypes.Orthographic
                : ProjectionTypes.Perspective;
        }

        previewWireframeEnabled = viewModel.PreviewWireframe;
        previewPhysicsEnabled = viewModel.PreviewPhysicsOverlay && previewPhysicsNodes is not null;
        UpdatePreviewWireframe();
        UpdatePreviewPhysicsWireframe();
        RenderPreviewScene();
    }

    private void PreviewGizmo_ViewRequested(object? sender, PreviewGizmoView view)
    {
        switch (view)
        {
            case PreviewGizmoView.PositiveX:
                SetPreviewCamera(heading: 90, attitude: 0, fit: true);
                break;

            case PreviewGizmoView.NegativeX:
                SetPreviewCamera(heading: -90, attitude: 0, fit: true);
                break;

            case PreviewGizmoView.PositiveY:
                SetPreviewCamera(heading: 0, attitude: 0, fit: true);
                break;

            case PreviewGizmoView.NegativeY:
                SetPreviewCamera(heading: 180, attitude: 0, fit: true);
                break;

            case PreviewGizmoView.PositiveZ:
                SetPreviewCamera(heading: 0, attitude: -90, fit: true);
                break;

            case PreviewGizmoView.NegativeZ:
                SetPreviewCamera(heading: 0, attitude: 90, fit: true);
                break;
        }
    }

    private void SetPreviewCamera(float heading, float attitude, bool fit)
    {
        if (previewCamera is null)
        {
            return;
        }

        previewCamera.Heading = heading;
        previewCamera.Attitude = attitude;
        if (fit)
        {
            FitPreviewToModel();
        }
        else
        {
            RenderPreviewScene();
        }
    }

    private void FitPreviewToModel()
    {
        if (previewCamera is null || previewModelNodes is null)
        {
            return;
        }

        if (previewModelNodes.WorldBoundingBox.IsUndefined)
        {
            previewModelNodes.Update();
        }

        if (!previewModelNodes.WorldBoundingBox.IsUndefined)
        {
            var fitted = previewCamera.FitIntoView(previewModelNodes, FitIntoViewType.CheckBounds, true, 1.15f, false);
            if (!fitted)
            {
                previewCamera.TargetPosition = previewModelNodes.WorldBoundingBox.GetCenterPosition();
                previewCamera.Distance = MathF.Max(previewModelNodes.WorldBoundingBox.GetDiagonalLength() * 1.5f, 1.0f);
            }
        }

        RenderPreviewScene();
    }

    private void UpdatePreviewWireframe()
    {
        if (previewWireframeNode is not null)
        {
            PreviewSceneView.Scene.RootNode.Remove(previewWireframeNode);
            previewWireframeNode.Dispose();
            previewWireframeNode = null;
        }

        if (!previewWireframeEnabled || previewModelNodes is null)
        {
            return;
        }

        var positions = LineUtils.GetWireframeLinePositions(previewModelNodes, removedDuplicateLines: false);
        if (positions.Length == 0)
        {
            return;
        }

        var material = new LineMaterial(Color3.White, lineThickness: 1.0f)
        {
            DepthBias = 0.002f
        };

        previewWireframeNode = new MultiLineNode(isLineStrip: false, material, "PreviewWireframe")
        {
            Positions = positions
        };
        PreviewSceneView.Scene.RootNode.Add(previewWireframeNode);
    }

    private void UpdatePreviewPhysicsWireframe()
    {
        if (previewPhysicsWireframeNode is not null)
        {
            PreviewSceneView.Scene.RootNode.Remove(previewPhysicsWireframeNode);
            previewPhysicsWireframeNode.Dispose();
            previewPhysicsWireframeNode = null;
        }

        if (!previewPhysicsEnabled || previewPhysicsNodes is null)
        {
            return;
        }

        var positions = LineUtils.GetWireframeLinePositions(previewPhysicsNodes, removedDuplicateLines: true);
        if (positions.Length == 0)
        {
            return;
        }

        var material = new LineMaterial(new Color3(1.0f, 0.76f, 0.18f), lineThickness: 2.0f)
        {
            DepthBias = 0.004f
        };

        previewPhysicsWireframeNode = new MultiLineNode(isLineStrip: false, material, "PreviewPhysicsWireframe")
        {
            Positions = positions
        };
        PreviewSceneView.Scene.RootNode.Add(previewPhysicsWireframeNode);
    }

    private void RenderPreviewScene()
    {
        UpdatePreviewGizmo();
        PreviewSceneView.RenderScene(forceRender: true, forceUpdate: true);
    }

    private void UpdatePreviewGizmo()
    {
        if (previewCamera is null)
        {
            return;
        }

        PreviewGizmo.SetCamera(previewCamera.Heading, previewCamera.Attitude);
    }

    private void AppendLog(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.LogLines.Add(message);
            }
        });
    }

    private void SetStatus(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.StatusMessage = message;
            }
        });
    }

    private async void BrowseInputPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            await BrowseFileAsync("Select input model", GetInputFileTypes(viewModel.SelectedInputFormat.Value)) is { } path)
        {
            ApplyInputPath(viewModel, path);
        }
    }

    private async void BrowseAnimationPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            await BrowseFileAsync("Select animation file", [new FilePickerFileType("PSA animation") { Patterns = ["*.psa"] }]) is { } path)
        {
            viewModel.AnimationPath = path;
        }
    }

    private async void BrowseConfigPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            await BrowseFileAsync("Select config file", [new FilePickerFileType("GMConverter config") { Patterns = ["*.ini"] }]) is { } path)
        {
            viewModel.ConfigPath = path;
        }
    }

    private async void BrowseOutputPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            await BrowseFolderAsync("Select output folder") is { } path)
        {
            viewModel.OutputPath = path;
        }
    }

    private async void BrowseGameDirectory_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            await BrowseFolderAsync("Select Source game directory") is { } path)
        {
            viewModel.GameDirectory = path;
        }
    }

    private async void BrowseEngineDirectory_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            await BrowseFolderAsync("Select Source engine directory") is { } path)
        {
            viewModel.EngineDirectory = path;
        }
    }

    private async void BrowseMaterialDirectory_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            await BrowseFolderAsync("Select material search directory") is { } path)
        {
            viewModel.MaterialDirectory = path;
        }
    }

    private async void BrowseExplorerRootDirectory_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            await BrowseFolderAsync("Select asset root directory") is { } path)
        {
            viewModel.ExplorerRootDirectory = path;
        }
    }

    private async Task<string?> BrowseFileAsync(string title, IReadOnlyList<FilePickerFileType>? filters = null)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return null;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
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

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
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
