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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using GMConverter.UI.ViewModels;

namespace GMConverter.UI.Views;

public partial class PreviewPane : UserControl, IDisposable
{
    private enum PreviewShadingMode
    {
        Wireframe,
        Solid,
        Textured
    }

    private PreviewViewModel? _subscribedViewModel;
    private PointerCameraController? _previewCameraController;
    private TargetPositionCamera? _previewCamera;
    private GroupNode? _previewModelNodes;
    private GroupNode? _previewPhysicsNodes;
    private MultiLineNode? _previewWireframeNode;
    private MultiLineNode? _previewPhysicsWireframeNode;
    private readonly Dictionary<ModelNode, (Material? Material, Material? BackMaterial)> _previewOriginalMaterials = [];
    private readonly StandardMaterial _previewSolidMaterial = new(new Color3(0.64f, 0.67f, 0.72f), "PreviewSolidMaterial");
    private Color3 _previewWireframeColor = Color3.White;
    private bool _previewWireframeEnabled;
    private bool _previewPhysicsEnabled;
    private PreviewShadingMode _previewShadingMode = PreviewShadingMode.Textured;
    private bool _disposed;

    public PreviewPane()
    {
        InitializeComponent();
        InitializePreviewScene();
        ApplyPreviewTheme();
        Unloaded += (_, _) => Dispose();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property.Name == nameof(ActualThemeVariant))
        {
            ApplyPreviewTheme();
            UpdatePreviewWireframe();
            UpdatePreviewPhysicsWireframe();
            RenderPreviewScene();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        _subscribedViewModel?.PropertyChanged -= OnViewModelPropertyChanged;

        base.OnDataContextChanged(e);

        _subscribedViewModel = DataContext as PreviewViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyPreviewSettings(_subscribedViewModel);
        }
    }

    private void InitializePreviewScene()
    {
        _previewCamera = new TargetPositionCamera
        {
            Heading = -35,
            Attitude = -25,
            Distance = 200,
            TargetPosition = Vector3.Zero,
            ShowCameraLight = ShowCameraLightType.Always
        };

        PreviewSceneView.SceneView.Camera = _previewCamera;
        PreviewSceneView.Scene.SetAmbientLight(0.45f);
        PreviewSceneView.Scene.Lights.Add(new DirectionalLight(new Vector3(-0.45f, -0.75f, -0.35f)));
        PreviewSceneView.Scene.Lights.Add(new DirectionalLight(new Vector3(0.55f, -0.35f, 0.65f)));
        _previewCamera.CameraChanged += (_, _) => UpdatePreviewGizmo();
        PreviewGizmo.ViewRequested += PreviewGizmo_ViewRequested;
        UpdatePreviewGizmo();

        _previewCameraController = new PointerCameraController(PreviewSceneView)
        {
            RotateCameraConditions = PointerAndKeyboardConditions.LeftPointerButtonPressed,
            MoveCameraConditions = PointerAndKeyboardConditions.LeftPointerButtonPressed | PointerAndKeyboardConditions.ControlKey,
            QuickZoomConditions = PointerAndKeyboardConditions.LeftPointerButtonPressed | PointerAndKeyboardConditions.RightPointerButtonPressed,
            ZoomMode = CameraZoomMode.PointerPosition,
            RotateAroundPointerPosition = true,
            CameraSmoothing = CameraController.CameraSmoothingPresets.Normal
        };
    }

    private void ApplyPreviewTheme()
    {
        var backgroundColor = GetPreviewBackgroundColor();
        _previewWireframeColor = GetPreviewWireframeColor();
        PreviewSceneView.SceneView.BackgroundColor = backgroundColor;
        PreviewViewport.Background = new SolidColorBrush(ToAvaloniaColor(backgroundColor));

        RenderPreviewScene();
    }

    private Color4 GetPreviewBackgroundColor()
    {
        return ActualThemeVariant == ThemeVariant.Light
            ? new Color4(0.97f, 0.98f, 0.99f, 1.0f)
            : new Color4(0.02f, 0.025f, 0.035f, 1.0f);
    }

    private Color3 GetPreviewWireframeColor()
    {
        return ActualThemeVariant == ThemeVariant.Light
            ? new Color3(0.16f, 0.18f, 0.22f)
            : Color3.White;
    }

    private static Avalonia.Media.Color ToAvaloniaColor(Color4 color)
    {
        return Avalonia.Media.Color.FromArgb(
            ToByte(color.Alpha),
            ToByte(color.Red),
            ToByte(color.Green),
            ToByte(color.Blue));
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp(MathF.Round(value * byte.MaxValue), byte.MinValue, byte.MaxValue);
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PreviewViewModel.PreviewModelPath) &&
            sender is PreviewViewModel viewModel &&
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
        _previewModelNodes = null;
        _previewPhysicsNodes = null;
        _previewWireframeNode = null;
        _previewPhysicsWireframeNode = null;
        _previewOriginalMaterials.Clear();
        _previewWireframeEnabled = DataContext is PreviewViewModel viewModel && viewModel.PreviewWireframe;
        _previewShadingMode = _previewWireframeEnabled ? PreviewShadingMode.Wireframe : PreviewShadingMode.Textured;
        UpdateShadingButtonState();
        _previewPhysicsEnabled = false;

        var modelNodes = await ImportPreviewNodesAsync(modelPath);
        if (modelNodes is null)
        {
            return;
        }

        _previewModelNodes = modelNodes;
        PreviewSceneView.Scene.RootNode.Add(modelNodes);
        CapturePreviewModelMaterials();

        if (modelNodes.WorldBoundingBox.IsUndefined)
        {
            modelNodes.Update();
        }

        if (!string.IsNullOrWhiteSpace(physicsModelPath) && File.Exists(physicsModelPath))
        {
            _previewPhysicsNodes = await ImportPreviewNodesAsync(physicsModelPath);
            if (_previewPhysicsNodes is not null)
            {
                _previewPhysicsNodes.SetVisibility(false);
                PreviewSceneView.Scene.RootNode.Add(_previewPhysicsNodes);
                if (_previewPhysicsNodes.WorldBoundingBox.IsUndefined)
                {
                    _previewPhysicsNodes.Update();
                }
            }
        }

        _previewPhysicsEnabled = DataContext is PreviewViewModel currentViewModel &&
            currentViewModel.PreviewPhysicsOverlay &&
            _previewPhysicsNodes is not null;

        UpdatePreviewWireframe();
        UpdatePreviewPhysicsWireframe();
        ApplyPreviewShadingMode();
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

    private void PreviewProjection_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_previewCamera is null)
        {
            return;
        }

        var useOrthographic = PreviewProjectionToggle.IsChecked is true;
        if (DataContext is PreviewViewModel viewModel)
        {
            viewModel.PreviewOrthographic = useOrthographic;
        }

        _previewCamera.ProjectionType = useOrthographic
            ? ProjectionTypes.Orthographic
            : ProjectionTypes.Perspective;
        UpdateProjectionButtonState(useOrthographic);
        FitPreviewToModel();
    }

    private void PreviewWireframe_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetPreviewShadingMode(PreviewShadingMode.Wireframe);
    }

    private void PreviewSolid_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetPreviewShadingMode(PreviewShadingMode.Solid);
    }

    private void PreviewTextured_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetPreviewShadingMode(PreviewShadingMode.Textured);
    }

    private void PreviewPhysics_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var showPhysics = PreviewPhysicsToggle.IsChecked is true;
        if (DataContext is PreviewViewModel viewModel)
        {
            viewModel.PreviewPhysicsOverlay = showPhysics;
        }

        _previewPhysicsEnabled = showPhysics && _previewPhysicsNodes is not null;
        UpdatePreviewPhysicsWireframe();
        RenderPreviewScene();
    }

    private void ApplyPreviewSettings(PreviewViewModel viewModel)
    {
        PreviewProjectionToggle.IsChecked = viewModel.PreviewOrthographic;
        PreviewPhysicsToggle.IsChecked = viewModel.PreviewPhysicsOverlay;

        _previewCamera?.ProjectionType = viewModel.PreviewOrthographic
                ? ProjectionTypes.Orthographic
                : ProjectionTypes.Perspective;

        _previewWireframeEnabled = viewModel.PreviewWireframe;
        _previewShadingMode = _previewWireframeEnabled ? PreviewShadingMode.Wireframe : PreviewShadingMode.Textured;
        _previewPhysicsEnabled = viewModel.PreviewPhysicsOverlay && _previewPhysicsNodes is not null;
        UpdateProjectionButtonState(viewModel.PreviewOrthographic);
        UpdateShadingButtonState();
        UpdatePreviewWireframe();
        ApplyPreviewShadingMode();
        UpdatePreviewPhysicsWireframe();
        RenderPreviewScene();
    }

    private void SetPreviewShadingMode(PreviewShadingMode shadingMode)
    {
        _previewShadingMode = shadingMode;
        _previewWireframeEnabled = shadingMode == PreviewShadingMode.Wireframe;
        if (DataContext is PreviewViewModel viewModel)
        {
            viewModel.PreviewWireframe = _previewWireframeEnabled;
        }

        UpdateShadingButtonState();
        UpdatePreviewWireframe();
        ApplyPreviewShadingMode();
        RenderPreviewScene();
    }

    private void UpdateProjectionButtonState(bool useOrthographic)
    {
        PreviewProjectionToggle.IsChecked = useOrthographic;
        PreviewOrthographicIcon.IsVisible = useOrthographic;
        PreviewPerspectiveIcon.IsVisible = !useOrthographic;
    }

    private void UpdateShadingButtonState()
    {
        PreviewWireframeToggle.IsChecked = _previewShadingMode == PreviewShadingMode.Wireframe;
        PreviewSolidToggle.IsChecked = _previewShadingMode == PreviewShadingMode.Solid;
        PreviewTexturedToggle.IsChecked = _previewShadingMode == PreviewShadingMode.Textured;
    }

    private void CapturePreviewModelMaterials()
    {
        _previewOriginalMaterials.Clear();
        if (_previewModelNodes is null)
        {
            return;
        }

        foreach (var modelNode in _previewModelNodes.GetAllChildren<ModelNode>("*", int.MaxValue))
        {
            _previewOriginalMaterials[modelNode] = (modelNode.Material, modelNode.BackMaterial);
        }
    }

    private void ApplyPreviewShadingMode()
    {
        if (_previewModelNodes is null)
        {
            return;
        }

        if (_previewShadingMode == PreviewShadingMode.Solid)
        {
            ApplySolidPreviewMaterials();
        }
        else
        {
            RestorePreviewMaterials();
        }

        _previewModelNodes.SetVisibility(_previewShadingMode != PreviewShadingMode.Wireframe);
    }

    private void ApplySolidPreviewMaterials()
    {
        foreach (var (modelNode, originalMaterials) in _previewOriginalMaterials)
        {
            modelNode.Material = _previewSolidMaterial;
            if (originalMaterials.BackMaterial is not null)
            {
                modelNode.BackMaterial = _previewSolidMaterial;
            }

            modelNode.NotifyChange(SceneNodeDirtyFlags.MaterialChanged);
        }
    }

    private void RestorePreviewMaterials()
    {
        foreach (var (modelNode, originalMaterials) in _previewOriginalMaterials)
        {
            modelNode.Material = originalMaterials.Material;
            modelNode.BackMaterial = originalMaterials.BackMaterial;
            modelNode.NotifyChange(SceneNodeDirtyFlags.MaterialChanged);
        }
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
        if (_previewCamera is null)
        {
            return;
        }

        _previewCamera.Heading = heading;
        _previewCamera.Attitude = attitude;
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
        if (_previewCamera is null || _previewModelNodes is null)
        {
            return;
        }

        if (_previewModelNodes.WorldBoundingBox.IsUndefined)
        {
            _previewModelNodes.Update();
        }

        if (!_previewModelNodes.WorldBoundingBox.IsUndefined)
        {
            var fitted = _previewCamera.FitIntoView(_previewModelNodes, FitIntoViewType.CheckBounds, true, 1.15f, false);
            if (!fitted)
            {
                _previewCamera.TargetPosition = _previewModelNodes.WorldBoundingBox.GetCenterPosition();
                _previewCamera.Distance = MathF.Max(_previewModelNodes.WorldBoundingBox.GetDiagonalLength() * 1.5f, 1.0f);
            }
        }

        RenderPreviewScene();
    }

    private void UpdatePreviewWireframe()
    {
        if (_previewWireframeNode is not null)
        {
            PreviewSceneView.Scene.RootNode.Remove(_previewWireframeNode);
            _previewWireframeNode.Dispose();
            _previewWireframeNode = null;
        }

        if (!_previewWireframeEnabled || _previewModelNodes is null)
        {
            return;
        }

        var positions = LineUtils.GetWireframeLinePositions(_previewModelNodes, removedDuplicateLines: false);
        if (positions.Length == 0)
        {
            return;
        }

        var material = new LineMaterial(_previewWireframeColor, lineThickness: 1.0f)
        {
            DepthBias = 0.002f
        };

        _previewWireframeNode = new MultiLineNode(isLineStrip: false, material, "PreviewWireframe")
        {
            Positions = positions
        };
        PreviewSceneView.Scene.RootNode.Add(_previewWireframeNode);
    }

    private void UpdatePreviewPhysicsWireframe()
    {
        if (_previewPhysicsWireframeNode is not null)
        {
            PreviewSceneView.Scene.RootNode.Remove(_previewPhysicsWireframeNode);
            _previewPhysicsWireframeNode.Dispose();
            _previewPhysicsWireframeNode = null;
        }

        if (!_previewPhysicsEnabled || _previewPhysicsNodes is null)
        {
            return;
        }

        var positions = LineUtils.GetWireframeLinePositions(_previewPhysicsNodes, removedDuplicateLines: true);
        if (positions.Length == 0)
        {
            return;
        }

        var material = new LineMaterial(new Color3(1.0f, 0.76f, 0.18f), lineThickness: 2.0f)
        {
            DepthBias = 0.004f
        };

        _previewPhysicsWireframeNode = new MultiLineNode(isLineStrip: false, material, "PreviewPhysicsWireframe")
        {
            Positions = positions
        };
        PreviewSceneView.Scene.RootNode.Add(_previewPhysicsWireframeNode);
    }

    private void RenderPreviewScene()
    {
        UpdatePreviewGizmo();
        PreviewSceneView.RenderScene(forceRender: true, forceUpdate: true);
    }

    private void UpdatePreviewGizmo()
    {
        if (_previewCamera is null)
        {
            return;
        }

        PreviewGizmo.SetCamera(_previewCamera.Heading, _previewCamera.Attitude);
    }

    private void AppendLog(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is PreviewViewModel viewModel)
            {
                viewModel.AppendLog(message);
            }
        });
    }

    private void SetStatus(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is PreviewViewModel viewModel)
            {
                viewModel.StatusMessage = message;
            }
        });
    }

    public void Dispose()
    {
        DisposePreviewResources();
        GC.SuppressFinalize(this);
    }

    private void DisposePreviewResources()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_subscribedViewModel is { } subscribedViewModel)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        PreviewGizmo.ViewRequested -= PreviewGizmo_ViewRequested;
        _previewWireframeNode?.Dispose();
        _previewWireframeNode = null;
        _previewPhysicsWireframeNode?.Dispose();
        _previewPhysicsWireframeNode = null;
        PreviewSceneView.Dispose();
        _previewCameraController = null;
        _previewCamera = null;
    }
}
