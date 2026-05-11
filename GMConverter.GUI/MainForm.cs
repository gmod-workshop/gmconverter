using System.Numerics;
using GMConverter.Common;
using GMConverter.Exporters;
using GMConverter.GameExplorer;
using GMConverter.Geometry;
using GMConverter.Importers;
using GMConverter.Source;
using Microsoft.Extensions.Logging;
using GeometryBounds = GMConverter.Geometry.Bounds;

namespace GMConverter.GUI;

internal sealed class MainForm : Form
{
    private const int InputPaneMinimumWidth = 760;
    private const int PreviewPaneMinimumWidth = 220;
    private const float SourceUnitsPerMeter = 39.3700787f;

    private readonly ComboBox _inputFormatBox = new();
    private readonly ComboBox _outputFormatBox = new();
    private readonly TextBox _configPathBox = new();
    private readonly TextBox _inputPathBox = new();
    private readonly TextBox _outputPathBox = new();
    private readonly TextBox _nameBox = new();
    private readonly TextBox _modelPathBox = new();
    private readonly TextBox _gameDirectoryBox = new();
    private readonly TextBox _engineDirectoryBox = new();
    private readonly TextBox _materialDirectoryBox = new();
    private readonly TextBox _animationPathBox = new();
    private readonly NumericUpDown _scaleBox = new();
    private readonly ComboBox _axisModeBox = new();
    private readonly CheckBox _noMaterialsBox = new();
    private readonly CheckBox _physicsBox = new();
    private readonly ComboBox _physicsModeBox = new();
    private readonly NumericUpDown _physicsMassBox = new();
    private readonly NumericUpDown _coacdThresholdBox = new();
    private readonly NumericUpDown _maxConvexPiecesBox = new();
    private readonly NumericUpDown _maxHullVerticesBox = new();
    private readonly Button _runButton = new();
    private readonly Button _loadConfigButton = new();
    private readonly Button _previewButton = new();
    private readonly CheckBox _previewPhysicsBox = new();
    private readonly PreviewViewport _previewViewport = new();
    private readonly Label _previewStatsLabel = new();
    private readonly TextBox _logBox = new();
    private readonly GameExplorerService _gameExplorerService = new();
    private readonly TextBox _explorerGameDirectoryBox = new();
    private readonly ComboBox _explorerGameBox = new();
    private readonly Button _explorerScanButton = new();
    private readonly Button _explorerPreviewButton = new();
    private readonly Button _explorerExportButton = new();
    private readonly TreeView _explorerTree = new();
    private readonly Label _explorerStatusLabel = new();

    private static readonly FormatDisplayItem[] InputFormats =
    [
        new("opt", "OPT", new OPTImporter().InputName),
        new("mdl", "MDL", new MDLImporter().InputName),
        new("psk", "PSK", new PSKImporter().InputName),
        new("mow", "MOW", new MOWImporter().InputName)
    ];

    private static readonly FormatDisplayItem[] OutputFormats =
    [
        new("info", "Info", "Summary"),
        new(new OBJExporter().OutputFormat, "OBJ", new OBJExporter().OutputName),
        new("glb", "GLB", new GLTFExporter().OutputName),
        new("gltf", "glTF", new GLTFExporter().OutputName),
        new("source", "Source", new MDLExporter().OutputName),
        new(new MDLExporter().OutputFormat, "MDL", new MDLExporter().OutputName)
    ];

    public MainForm()
    {
        Text = "GMConverter";
        MinimumSize = new Size(1180, 720);
        StartPosition = FormStartPosition.CenterScreen;

        ConfigureControls();
        Controls.Add(BuildLayout());
        TryLoadDefaultConfig();
        UpdateControlState();
    }

    private void ConfigureControls()
    {
        _inputFormatBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _inputFormatBox.Items.AddRange(InputFormats.Cast<object>().ToArray());
        _inputFormatBox.SelectedIndex = 0;
        _inputFormatBox.SelectedIndexChanged += (_, _) => UpdateControlState();

        _outputFormatBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _outputFormatBox.Items.AddRange(OutputFormats.Cast<object>().ToArray());
        SetComboValue(_outputFormatBox, "mdl", "output-format");
        _outputFormatBox.SelectedIndexChanged += (_, _) => UpdateControlState();

        _scaleBox.DecimalPlaces = 4;
        _scaleBox.Minimum = 0.0001m;
        _scaleBox.Maximum = 100000m;
        _scaleBox.Value = 1m;
        _scaleBox.Increment = 0.1m;

        _axisModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _axisModeBox.Items.AddRange(["auto", "z-up", "y-up"]);
        _axisModeBox.SelectedItem = "auto";

        _noMaterialsBox.Text = "Skip material compilation";

        _physicsBox.Text = "Generate physics mesh";
        _physicsBox.CheckedChanged += (_, _) => UpdateControlState();

        _physicsModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _physicsModeBox.Items.AddRange(["bounds", "coacd"]);
        _physicsModeBox.SelectedItem = "bounds";
        _physicsModeBox.SelectedIndexChanged += (_, _) => UpdateControlState();

        _physicsMassBox.Minimum = 0.1m;
        _physicsMassBox.Maximum = 100000m;
        _physicsMassBox.DecimalPlaces = 1;
        _physicsMassBox.Value = 100m;
        _physicsMassBox.Increment = 10m;

        _coacdThresholdBox.Minimum = 0.01m;
        _coacdThresholdBox.Maximum = 1m;
        _coacdThresholdBox.DecimalPlaces = 2;
        _coacdThresholdBox.Value = 0.01m;
        _coacdThresholdBox.Increment = 0.01m;

        _maxConvexPiecesBox.Minimum = -1m;
        _maxConvexPiecesBox.Maximum = 1024m;
        _maxConvexPiecesBox.Value = 32m;

        _maxHullVerticesBox.Minimum = 4m;
        _maxHullVerticesBox.Maximum = 1024m;
        _maxHullVerticesBox.Value = 32m;

        _modelPathBox.Text = "gmconverter/model.mdl";

        _loadConfigButton.Text = "Load Config";
        _loadConfigButton.Height = 32;
        _loadConfigButton.Click += (_, _) => LoadConfigFromPath(_configPathBox.Text);

        _runButton.Text = "Run Conversion";
        _runButton.Height = 36;
        _runButton.Click += async (_, _) => await RunConversionAsync();

        _previewButton.Text = "Preview";
        _previewButton.Height = 32;
        _previewButton.Click += async (_, _) => await LoadPreviewAsync();

        _previewPhysicsBox.Text = "Show physics mesh";

        _previewViewport.Dock = DockStyle.Fill;
        _previewViewport.MinimumSize = new Size(PreviewPaneMinimumWidth, 220);

        _previewStatsLabel.AutoSize = false;
        _previewStatsLabel.Dock = DockStyle.Fill;
        _previewStatsLabel.MinimumSize = new Size(PreviewPaneMinimumWidth, 58);
        _previewStatsLabel.Padding = new Padding(8, 6, 8, 6);
        _previewStatsLabel.BackColor = Color.FromArgb(31, 34, 39);
        _previewStatsLabel.ForeColor = Color.FromArgb(220, 224, 230);
        _previewStatsLabel.Font = new Font(FontFamily.GenericMonospace, 8.5f);
        _previewStatsLabel.Text = "No preview loaded.";

        _logBox.Multiline = true;
        _logBox.ScrollBars = ScrollBars.Both;
        _logBox.ReadOnly = true;
        _logBox.WordWrap = false;
        _logBox.Dock = DockStyle.Fill;
        _logBox.MinimumSize = new Size(0, 180);
        _logBox.Font = new Font(FontFamily.GenericMonospace, 9f);

        _explorerGameBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _explorerGameBox.Items.Add(new GameProfileDisplayItem(GameExplorerService.AutoProfileId, "Auto-detect"));
        _explorerGameBox.Items.AddRange(_gameExplorerService.Profiles
            .Select(profile => new GameProfileDisplayItem(profile.Id, profile.DisplayName))
            .Cast<object>()
            .ToArray());
        _explorerGameBox.SelectedIndex = 0;

        _explorerScanButton.Text = "Scan";
        _explorerScanButton.Height = 32;
        _explorerScanButton.Click += async (_, _) => await ScanGameExplorerAsync();

        _explorerPreviewButton.Text = "Preview Selected";
        _explorerPreviewButton.Height = 32;
        _explorerPreviewButton.Enabled = false;
        _explorerPreviewButton.Click += async (_, _) => await PreviewSelectedExplorerEntryAsync();

        _explorerExportButton.Text = "Export Selected";
        _explorerExportButton.Height = 32;
        _explorerExportButton.Enabled = false;
        _explorerExportButton.Click += async (_, _) => await ExportSelectedExplorerEntryAsync();

        _explorerTree.Dock = DockStyle.Fill;
        _explorerTree.HideSelection = false;
        _explorerTree.AfterSelect += async (_, args) =>
        {
            UpdateExplorerSelectionState();
            if (args.Node?.Tag is GameExplorerEntry)
            {
                await PreviewSelectedExplorerEntryAsync();
            }
        };

        _explorerStatusLabel.AutoSize = false;
        _explorerStatusLabel.Dock = DockStyle.Fill;
        _explorerStatusLabel.Padding = new Padding(6);
        _explorerStatusLabel.Text = "Select a game directory and scan for supported models.";

        EnablePathDrop(_inputPathBox, DirectoryDropBehavior.FileOrDirectory);
        EnablePathDrop(_outputPathBox, DirectoryDropBehavior.Directory);
        EnablePathDrop(_configPathBox, DirectoryDropBehavior.FileOrDirectory);
        EnablePathDrop(_gameDirectoryBox, DirectoryDropBehavior.Directory);
        EnablePathDrop(_engineDirectoryBox, DirectoryDropBehavior.Directory);
        EnablePathDrop(_materialDirectoryBox, DirectoryDropBehavior.Directory);
        EnablePathDrop(_animationPathBox, DirectoryDropBehavior.FileOrDirectory);
        EnablePathDrop(_explorerGameDirectoryBox, DirectoryDropBehavior.Directory);
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(InputPaneMinimumWidth, 0)
        };
        var convertTab = new TabPage("Convert");
        var explorerTab = new TabPage("Game Explorer");

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            MinimumSize = new Size(InputPaneMinimumWidth, 0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var sections = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            MinimumSize = new Size(InputPaneMinimumWidth, 0)
        };
        sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var formatSection = CreateSection("Format");
        AddRow(formatSection, "Input format", _inputFormatBox);
        AddRow(formatSection, "Output format", _outputFormatBox);

        var fileSection = CreateSection("File Paths");
        AddPathRow(fileSection, "Input path", _inputPathBox, BrowseInputFile);
        AddPathRow(fileSection, "Animation path", _animationPathBox, BrowseAnimationFile);
        AddPathRow(fileSection, "Output path", _outputPathBox, BrowseOutputFolder);
        AddRow(fileSection, "Name", _nameBox);

        var toolSection = CreateSection("Tool Paths");
        AddPathRow(toolSection, "Config file", _configPathBox, BrowseConfigFile);
        AddRow(toolSection, "Model path", _modelPathBox);
        AddPathRow(toolSection, "Game directory", _gameDirectoryBox, BrowseFolder);
        AddPathRow(toolSection, "Engine directory", _engineDirectoryBox, BrowseFolder);
        AddPathRow(toolSection, "Material directory", _materialDirectoryBox, BrowseFolder);

        var geometrySection = CreateSection("Geometry");
        AddRow(geometrySection, "Scale", _scaleBox);
        AddRow(geometrySection, "Input axes", _axisModeBox);
        AddCheckRow(geometrySection, _noMaterialsBox);

        var physicsSection = CreateSection("Physics");
        AddCheckRow(physicsSection, _physicsBox);
        AddRow(physicsSection, "Physics mode", _physicsModeBox);
        AddRow(physicsSection, "Physics mass", _physicsMassBox);
        AddRow(physicsSection, "CoACD threshold", _coacdThresholdBox);
        AddRow(physicsSection, "Max convex pieces", _maxConvexPiecesBox);
        AddRow(physicsSection, "Max hull vertices", _maxHullVerticesBox);

        var actionSection = CreateSection("Run");
        _loadConfigButton.Dock = DockStyle.Fill;
        _loadConfigButton.Margin = new Padding(0, 4, 0, 0);
        actionSection.Controls.Add(_loadConfigButton, 0, actionSection.RowCount);
        actionSection.SetColumnSpan(_loadConfigButton, 3);
        actionSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionSection.RowCount++;
        _runButton.Dock = DockStyle.Fill;
        _runButton.Margin = new Padding(0, 4, 0, 0);
        actionSection.Controls.Add(_runButton, 0, actionSection.RowCount);
        actionSection.SetColumnSpan(_runButton, 3);
        actionSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionSection.RowCount++;
        _previewButton.Dock = DockStyle.Fill;
        _previewButton.Margin = new Padding(0, 8, 0, 0);
        actionSection.Controls.Add(_previewButton, 0, actionSection.RowCount);
        actionSection.SetColumnSpan(_previewButton, 3);
        actionSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionSection.RowCount++;
        AddCheckRow(actionSection, _previewPhysicsBox);

        AddSection(sections, formatSection, 0, 0);
        AddSection(sections, fileSection, 0, 1);
        AddSection(sections, toolSection, 0, 2);
        AddSection(sections, geometrySection, 1, 0);
        AddSection(sections, physicsSection, 1, 1);
        AddSection(sections, actionSection, 1, 2);

        var outputSection = CreateOutputSection();
        var previewSection = CreatePreviewSection();

        left.Controls.Add(sections, 0, 0);
        left.Controls.Add(outputSection, 0, 1);
        convertTab.Controls.Add(left);
        explorerTab.Controls.Add(BuildGameExplorerPanel());
        tabs.TabPages.Add(convertTab);
        tabs.TabPages.Add(explorerTab);

        root.Controls.Add(tabs, 0, 0);
        root.Controls.Add(previewSection, 1, 0);

        return root;
    }

    private Control BuildGameExplorerPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var explorerSection = CreateSection("Game Explorer");
        AddPathRow(explorerSection, "Game directory", _explorerGameDirectoryBox, BrowseFolder);
        AddRow(explorerSection, "Game", _explorerGameBox);
        _explorerScanButton.Dock = DockStyle.Fill;
        _explorerScanButton.Margin = new Padding(0, 8, 0, 0);
        explorerSection.Controls.Add(_explorerScanButton, 0, explorerSection.RowCount);
        explorerSection.SetColumnSpan(_explorerScanButton, 3);
        explorerSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        explorerSection.RowCount++;

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _explorerPreviewButton.Dock = DockStyle.Fill;
        _explorerExportButton.Dock = DockStyle.Fill;
        _explorerPreviewButton.Margin = new Padding(0, 0, 6, 0);
        _explorerExportButton.Margin = new Padding(6, 0, 0, 0);
        actions.Controls.Add(_explorerPreviewButton, 0, 0);
        actions.Controls.Add(_explorerExportButton, 1, 0);

        panel.Controls.Add((Control)explorerSection.Tag!, 0, 0);
        panel.Controls.Add(_explorerStatusLabel, 0, 1);
        panel.Controls.Add(_explorerTree, 0, 2);
        panel.Controls.Add(actions, 0, 3);

        return panel;
    }

    private static TableLayoutPanel CreateSection(string title)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 12, 12)
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        group.Controls.Add(panel);
        panel.Tag = group;
        return panel;
    }

    private GroupBox CreateOutputSection()
    {
        var group = new GroupBox
        {
            Text = "Output",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            Margin = new Padding(0)
        };

        group.Controls.Add(_logBox);
        return group;
    }

    private GroupBox CreatePreviewSection()
    {
        var group = new GroupBox
        {
            Text = "Preview",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            Margin = new Padding(12, 0, 0, 0),
            MinimumSize = new Size(PreviewPaneMinimumWidth, 0)
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        panel.Controls.Add(_previewViewport, 0, 0);
        panel.Controls.Add(_previewStatsLabel, 0, 1);

        group.Controls.Add(panel);
        return group;
    }

    private static void AddSection(TableLayoutPanel sections, TableLayoutPanel section, int column, int row)
    {
        while (sections.RowCount <= row)
        {
            sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sections.RowCount++;
        }

        var group = (Control)section.Tag!;
        if (column == sections.ColumnCount - 1)
        {
            group.Margin = new Padding(0, 0, 0, 12);
        }

        sections.Controls.Add(group, column, row);
    }

    private static void AddRow(TableLayoutPanel form, string label, Control control)
    {
        var row = form.RowCount++;
        form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        form.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 0) }, 0, row);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 4, 8, 0);
        form.Controls.Add(control, 1, row);
        form.SetColumnSpan(control, 2);
    }

    private static void AddCheckRow(TableLayoutPanel form, CheckBox control)
    {
        var row = form.RowCount++;
        form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.AutoSize = true;
        control.Margin = new Padding(0, 6, 8, 0);
        form.Controls.Add(control, 1, row);
        form.SetColumnSpan(control, 2);
    }

    private static void AddPathRow(TableLayoutPanel form, string label, TextBox textBox, Func<string?> browse)
    {
        var row = form.RowCount++;
        form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        form.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 0) }, 0, row);
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(0, 4, 8, 0);
        form.Controls.Add(textBox, 1, row);

        var button = new Button { Text = "Browse", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
        button.Click += (_, _) =>
        {
            var selected = browse();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                textBox.Text = selected;
            }
        };
        form.Controls.Add(button, 2, row);
    }

    private void UpdateControlState()
    {
        var outputFormat = SelectedText(_outputFormatBox);
        var inputFormat = SelectedText(_inputFormatBox);
        var writesFiles = outputFormat is not "info";
        var sourceOutput = outputFormat is "source" or "mdl";
        var pskInput = inputFormat is "psk";
        var physicsEnabled = sourceOutput && (_physicsBox.Checked || SelectedText(_physicsModeBox) is "coacd");
        var coacdEnabled = sourceOutput && SelectedText(_physicsModeBox) is "coacd";

        _animationPathBox.Enabled = pskInput;
        _outputPathBox.Enabled = writesFiles;
        _nameBox.Enabled = writesFiles;
        _modelPathBox.Enabled = sourceOutput;
        _gameDirectoryBox.Enabled = sourceOutput;
        _engineDirectoryBox.Enabled = sourceOutput;
        _noMaterialsBox.Enabled = sourceOutput;
        _physicsBox.Enabled = sourceOutput;
        _physicsModeBox.Enabled = sourceOutput;
        _physicsMassBox.Enabled = physicsEnabled;
        _coacdThresholdBox.Enabled = coacdEnabled;
        _maxConvexPiecesBox.Enabled = coacdEnabled;
        _maxHullVerticesBox.Enabled = coacdEnabled;
        _previewPhysicsBox.Enabled = sourceOutput && (_physicsBox.Checked || SelectedText(_physicsModeBox) is "coacd");
    }

    private void TryLoadDefaultConfig()
    {
        var defaultPath = Config.FindDefaultPath();
        if (defaultPath is null)
        {
            return;
        }

        _configPathBox.Text = defaultPath;
        LoadConfigFromPath(defaultPath);
    }

    private void LoadConfigFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            AppendLog("Config path is empty.");
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            ApplyConfig(Config.Load(fullPath));
            _configPathBox.Text = fullPath;
            AppendLog($"Loaded config: {fullPath}");
        }
        catch (Exception ex)
        {
            AppendLog($"Config load failed. {ex.Message}");
        }
    }

    private void ApplyConfig(Config config)
    {
        SetComboValue(_inputFormatBox, config.InputFormat, "input-format");
        SetComboValue(_outputFormatBox, config.OutputFormat, "output-format");
        SetComboValue(_axisModeBox, config.AxisMode, "axis-mode");
        SetComboValue(_physicsModeBox, config.PhysicsMode, "physics-mode");

        SetText(_inputPathBox, config.InputPath);
        SetText(_outputPathBox, config.OutputPath);
        SetText(_nameBox, config.BaseName);
        SetText(_modelPathBox, config.ModelPath);
        SetText(_gameDirectoryBox, config.GameDirectory);
        SetText(_engineDirectoryBox, config.EngineDirectory);
        SetText(_materialDirectoryBox, config.MaterialDirectory);
        SetText(_animationPathBox, config.AnimationPath);

        SetNumericValue(_scaleBox, config.Scale);
        SetNumericValue(_physicsMassBox, config.PhysicsMass);
        SetNumericValue(_coacdThresholdBox, config.CoacdThreshold);
        SetNumericValue(_maxConvexPiecesBox, config.MaxConvexPieces);
        SetNumericValue(_maxHullVerticesBox, config.MaxHullVertices);

        if (config.NoScale is true)
        {
            _scaleBox.Value = 1m;
        }

        if (config.NoMaterials.HasValue)
        {
            _noMaterialsBox.Checked = config.NoMaterials.Value;
        }

        if (config.Physics.HasValue)
        {
            _physicsBox.Checked = config.Physics.Value;
        }

        UpdateControlState();
    }

    private async Task RunConversionAsync()
    {
        _runButton.Enabled = false;
        _logBox.Clear();

        try
        {
            AppendLog("Running conversion...");
            AppendLog("");

            var settings = CaptureConversionSettings();
            var summary = await Task.Run(() => RunConversion(settings));

            AppendLog(summary);
            AppendLog("");
            AppendLog("Conversion complete.");
        }
        catch (Exception ex)
        {
            AppendLog(ex.Message);
        }
        finally
        {
            _runButton.Enabled = true;
        }
    }

    private GuiConversionSettings CaptureConversionSettings()
    {
        return new GuiConversionSettings(
            SelectedText(_inputFormatBox),
            SelectedText(_outputFormatBox),
            _inputPathBox.Text,
            _outputPathBox.Enabled ? _outputPathBox.Text : null,
            string.IsNullOrWhiteSpace(_nameBox.Text) ? null : _nameBox.Text,
            _modelPathBox.Enabled ? EmptyToNull(_modelPathBox.Text) : null,
            _gameDirectoryBox.Enabled ? EmptyToNull(_gameDirectoryBox.Text) : null,
            _engineDirectoryBox.Enabled ? EmptyToNull(_engineDirectoryBox.Text) : null,
            EmptyToNull(_materialDirectoryBox.Text),
            _animationPathBox.Enabled ? EmptyToNull(_animationPathBox.Text) : null,
            (float)_scaleBox.Value,
            NormalizeAxisMode(SelectedText(_axisModeBox)),
            !(_noMaterialsBox.Enabled && _noMaterialsBox.Checked),
            _physicsBox.Enabled && _physicsBox.Checked,
            _physicsModeBox.Enabled && (_physicsBox.Checked || SelectedText(_physicsModeBox) is "coacd")
                ? SelectedText(_physicsModeBox)
                : null,
            (float)_physicsMassBox.Value,
            (float)_coacdThresholdBox.Value,
            (int)_maxConvexPiecesBox.Value,
            (int)_maxHullVerticesBox.Value);
    }

    private string? RunConversion(GuiConversionSettings settings)
    {
        var inputPath = RequireInputFile(settings.InputPath, settings.InputFormat);
        using var loggerFactory = CreateLoggerFactory();
        var importer = CreateImporter(settings.InputFormat, loggerFactory);

        if (settings.OutputFormat is "info")
        {
            return importer.Summarize(inputPath).ToString();
        }

        var outputPath = RequireOutputPath(settings.OutputPath, settings.OutputFormat);
        var baseName = string.IsNullOrWhiteSpace(settings.BaseName)
            ? Path.GetFileNameWithoutExtension(inputPath)
            : settings.BaseName;
        var model = importer.Parse(inputPath, new ModelParseOptions(
            settings.ScaleFactor,
            settings.AxisMode,
            CreateMaterialResolveOptions(settings.MaterialDirectory),
            CreateAnimationPath(settings.AnimationPath)));

        switch (settings.OutputFormat)
        {
            case "obj":
                Directory.CreateDirectory(outputPath);
                new OBJExporter().Export(model, outputPath, baseName, new OBJExportOptions());
                return $"Wrote OBJ output to {outputPath}";

            case "glb":
            case "gltf":
                Directory.CreateDirectory(outputPath);
                new GLTFExporter().Export(
                    model,
                    outputPath,
                    baseName,
                    new GLTFExportOptions(settings.OutputFormat is "glb"));
                return $"Wrote {(settings.OutputFormat is "glb" ? "GLB" : "glTF")} output to {outputPath}";

            case "source":
            case "mdl":
                Directory.CreateDirectory(outputPath);
                new MDLExporter().Export(
                    model,
                        outputPath,
                        baseName,
                        new MDLExportOptions(
                            settings.ModelPath ?? $"gmconverter/{SanitizePathToken(baseName)}.mdl",
                            settings.EngineDirectory,
                            settings.GameDirectory,
                            settings.BuildMaterials,
                            CreatePhysicsOptions(settings)));
                return $"Wrote Source compile workspace to {outputPath}";

            default:
                throw new GMConverterException("Unsupported output format.");
        }
    }

    private static string RequireInputFile(string path, string inputFormat)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        var extension = Path.GetExtension(fullPath);
        var allowedExtensions = inputFormat switch
        {
            "psk" => [".psk", ".pskx"],
            "mow" => [".def", ".mdl"],
            _ => new[] { $".{inputFormat}" }
        };

        if (!File.Exists(fullPath))
        {
            throw new GMConverterException($"File not found: {fullPath}");
        }

        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new GMConverterException($"Expected a {string.Join(" or ", allowedExtensions)} file: {fullPath}");
        }

        return fullPath;
    }

    private static string RequireOutputPath(string? path, string outputFormat)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new GMConverterException($"Output path is required for {outputFormat} output.");
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    private static PhysicsOptions? CreatePhysicsOptions(GuiConversionSettings settings)
    {
        if (!settings.GeneratePhysics && string.IsNullOrWhiteSpace(settings.PhysicsMode))
        {
            return null;
        }

        var mode = settings.PhysicsMode?.Trim().ToLowerInvariant() switch
        {
            null or "" or "bounds" => PhysicsMode.Bounds,
            "coacd" => PhysicsMode.Coacd,
            _ => throw new GMConverterException("Unsupported physics mode.")
        };

        if (mode is PhysicsMode.Bounds)
        {
            return new PhysicsOptions(mode, settings.PhysicsMass, null);
        }

        return new PhysicsOptions(
            mode,
            settings.PhysicsMass,
            new CoacdOptions(settings.CoacdThreshold, settings.MaxConvexPieces, settings.MaxHullVertices));
    }

    private static MaterialResolveOptions? CreateMaterialResolveOptions(string? materialDirectory)
    {
        if (string.IsNullOrWhiteSpace(materialDirectory))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(materialDirectory));
        if (!Directory.Exists(fullPath))
        {
            throw new GMConverterException($"Material directory not found: {fullPath}");
        }

        return new MaterialResolveOptions(fullPath);
    }

    private static string? CreateAnimationPath(string? animationPath)
    {
        if (string.IsNullOrWhiteSpace(animationPath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(animationPath));
        if (!File.Exists(fullPath))
        {
            throw new GMConverterException($"Animation file not found: {fullPath}");
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".psa", StringComparison.OrdinalIgnoreCase))
        {
            throw new GMConverterException($"Expected a .psa animation file: {fullPath}");
        }

        return fullPath;
    }

    private static string SanitizePathToken(string value)
    {
        return string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? char.ToLowerInvariant(c) : '_')).Trim('_');
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void SetText(TextBox textBox, string? value)
    {
        if (value is not null)
        {
            textBox.Text = value;
        }
    }

    private static void SetComboValue(ComboBox comboBox, string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var item in comboBox.Items)
        {
            if (item is FormatDisplayItem displayItem &&
                string.Equals(displayItem.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }

            if (string.Equals(item.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        throw new GMConverterException($"Unsupported configured {key}: {value}");
    }

    private static void SetNumericValue(NumericUpDown control, float? value)
    {
        if (value.HasValue)
        {
            SetNumericValue(control, (decimal)value.Value);
        }
    }

    private static void SetNumericValue(NumericUpDown control, int? value)
    {
        if (value.HasValue)
        {
            SetNumericValue(control, (decimal)value.Value);
        }
    }

    private static void SetNumericValue(NumericUpDown control, decimal value)
    {
        if (value < control.Minimum || value > control.Maximum)
        {
            throw new GMConverterException(
                $"Configured numeric value {value} is outside the allowed range {control.Minimum} to {control.Maximum}.");
        }

        control.Value = value;
    }

    private static ModelAxisMode NormalizeAxisMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "" or "auto" => ModelAxisMode.Auto,
            "z" or "z-up" or "zup" => ModelAxisMode.ZUp,
            "y" or "y-up" or "yup" => ModelAxisMode.YUp,
            _ => throw new GMConverterException("Unsupported input axis mode.")
        };
    }

    private async Task LoadPreviewAsync()
    {
        _previewButton.Enabled = false;

        try
        {
            var inputPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(_inputPathBox.Text));
            if (!File.Exists(inputPath))
            {
                AppendLog($"Preview failed. File not found: {inputPath}");
                return;
            }

            var inputFormat = SelectedText(_inputFormatBox);
            var scale = (float)_scaleBox.Value;
            var axisMode = NormalizeAxisMode(SelectedText(_axisModeBox));
            var materialOptions = CreateMaterialResolveOptions(EmptyToNull(_materialDirectoryBox.Text));
            var showPhysics = _previewPhysicsBox.Enabled && _previewPhysicsBox.Checked;
            var physicsOptions = showPhysics
                ? new PreviewPhysicsOptions(
                    SelectedText(_physicsModeBox),
                    (double)_coacdThresholdBox.Value,
                    (int)_maxConvexPiecesBox.Value,
                    (int)_maxHullVerticesBox.Value)
                : null;
            AppendLog(showPhysics ? "Loading preview with physics mesh..." : "Loading preview...");

            var result = await Task.Run(() =>
            {
                using var loggerFactory = CreateLoggerFactory();
                var importer = CreateImporter(inputFormat, loggerFactory);
                var model = importer.Parse(inputPath, new ModelParseOptions(
                    scale,
                    axisMode,
                    materialOptions));
                var physicsMeshes = physicsOptions is null ? [] : BuildPhysicsPreviewMeshes(model, physicsOptions);
                return new PreviewLoadResult(model, physicsMeshes);
            });

            _previewViewport.SetScene(result.Model, result.PhysicsMeshes);
            _previewStatsLabel.Text = PreviewStats.From(result.Model, result.PhysicsMeshes).ToString();
            AppendLog($"Preview loaded: {result.Model.Meshes.Count} mesh(es), {result.PhysicsMeshes.Count} physics part(s).");
        }
        catch (Exception ex)
        {
            _previewStatsLabel.Text = "Preview failed.";
            AppendLog($"Preview failed. {ex.Message}");
        }
        finally
        {
            _previewButton.Enabled = true;
        }
    }

    private static IReadOnlyList<Mesh> BuildPhysicsPreviewMeshes(Model model, PreviewPhysicsOptions options)
    {
        return options.Mode switch
        {
            "coacd" => BuildCoacdPreviewMeshes(model, options),
            _ => [CreateBoundsMesh(model.Bounds().WithMinimumThickness())]
        };
    }

    private static IReadOnlyList<Mesh> BuildCoacdPreviewMeshes(Model model, PreviewPhysicsOptions options)
    {
        return CoacdNative.Decompose(
            model.Merge(),
            new CoacdDecompositionOptions(
                options.CoacdThreshold,
                options.MaxConvexPieces,
                options.MaxHullVertices));
    }

    private static Mesh CreateBoundsMesh(GeometryBounds bounds)
    {
        Vector3[] positions =
        [
            new(bounds.Min.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Max.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Max.Z)
        ];

        var vertices = positions
            .Select(position => new Vertex(position, Vector3.UnitZ, Vector2.Zero))
            .ToArray();

        Triangle[] triangles =
        [
            new(0, 3, 2),
            new(0, 2, 1),
            new(4, 5, 6),
            new(4, 6, 7),
            new(0, 1, 5),
            new(0, 5, 4),
            new(3, 7, 6),
            new(3, 6, 2),
            new(0, 4, 7),
            new(0, 7, 3),
            new(1, 2, 6),
            new(1, 6, 5)
        ];

        return new Mesh(vertices, [new Submesh("physics", triangles)]);
    }

    private async Task ScanGameExplorerAsync()
    {
        _explorerScanButton.Enabled = false;
        _explorerPreviewButton.Enabled = false;
        _explorerExportButton.Enabled = false;
        _explorerTree.Nodes.Clear();

        try
        {
            var gameDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(_explorerGameDirectoryBox.Text));
            AppendLog($"Scanning game directory: {gameDirectory}");

            var profileId = SelectedGameProfileId();
            var result = await Task.Run(() => _gameExplorerService.Scan(gameDirectory, profileId));

            PopulateExplorerTree(result.Entries);
            _explorerStatusLabel.Text = $"{result.Profile.DisplayName}: {result.Entries.Count} supported model file(s).";
            AppendLog($"Game Explorer found {result.Entries.Count} supported model file(s) using {result.Profile.DisplayName}.");
        }
        catch (Exception ex)
        {
            _explorerStatusLabel.Text = "Scan failed.";
            AppendLog($"Game Explorer scan failed. {ex.Message}");
        }
        finally
        {
            _explorerScanButton.Enabled = true;
            UpdateExplorerSelectionState();
        }
    }

    private async Task PreviewSelectedExplorerEntryAsync()
    {
        if (SelectedExplorerEntry() is not { } entry)
        {
            return;
        }

        ApplyExplorerEntry(entry);
        await LoadPreviewAsync();
    }

    private async Task ExportSelectedExplorerEntryAsync()
    {
        if (SelectedExplorerEntry() is not { } entry)
        {
            return;
        }

        ApplyExplorerEntry(entry);
        await RunConversionAsync();
    }

    private void ApplyExplorerEntry(GameExplorerEntry entry)
    {
        var resolvedEntry = _gameExplorerService.ResolveEntry(entry);
        SetComboValue(_inputFormatBox, entry.InputFormat, "input-format");
        _inputPathBox.Text = resolvedEntry.InputPath;
        _materialDirectoryBox.Text = resolvedEntry.MaterialDirectory;

        var baseName = Path.GetFileNameWithoutExtension(resolvedEntry.InputPath);
        _nameBox.Text = baseName;
        if (_modelPathBox.Enabled || SelectedText(_outputFormatBox) is "source" or "mdl")
        {
            _modelPathBox.Text = $"gmconverter/{SanitizePathToken(baseName)}.mdl";
        }

        UpdateControlState();
    }

    private void PopulateExplorerTree(IEnumerable<GameExplorerEntry> entries)
    {
        _explorerTree.BeginUpdate();
        _explorerTree.Nodes.Clear();

        foreach (var entry in entries)
        {
            AddExplorerEntry(entry);
        }

        _explorerTree.EndUpdate();
        foreach (TreeNode node in _explorerTree.Nodes)
        {
            node.Expand();
        }
    }

    private void AddExplorerEntry(GameExplorerEntry entry)
    {
        var segments = entry.DisplayPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var nodes = _explorerTree.Nodes;
        TreeNode? current = null;

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var node = FindTreeNode(nodes, segment);
            if (node is null)
            {
                node = new TreeNode(segment);
                nodes.Add(node);
            }

            current = node;
            nodes = node.Nodes;
        }

        if (current is not null)
        {
            current.Tag = entry;
            current.ToolTipText = entry.FilePath;
        }
    }

    private static TreeNode? FindTreeNode(TreeNodeCollection nodes, string text)
    {
        foreach (TreeNode node in nodes)
        {
            if (string.Equals(node.Text, text, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }
        }

        return null;
    }

    private GameExplorerEntry? SelectedExplorerEntry()
    {
        return _explorerTree.SelectedNode?.Tag as GameExplorerEntry;
    }

    private void UpdateExplorerSelectionState()
    {
        var hasEntry = SelectedExplorerEntry() is not null;
        _explorerPreviewButton.Enabled = hasEntry;
        _explorerExportButton.Enabled = hasEntry;
    }

    private string SelectedGameProfileId()
    {
        return _explorerGameBox.SelectedItem is GameProfileDisplayItem item
            ? item.Id
            : GameExplorerService.AutoProfileId;
    }

    private static IImporter CreateImporter(string inputFormat, ILoggerFactory? loggerFactory = null)
    {
        return inputFormat switch
        {
            "opt" => new OPTImporter(),
            "mdl" => new MDLImporter(),
            "psk" => new PSKImporter(),
            "mow" => new MOWImporter(loggerFactory),
            _ => throw new InvalidOperationException($"Unsupported input format: {inputFormat}")
        };
    }

    private static string? BrowseInputFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Supported model files (*.opt;*.mdl;*.psk;*.pskx;*.def)|*.opt;*.mdl;*.psk;*.pskx;*.def|OPT files (*.opt)|*.opt|MDL files (*.mdl)|*.mdl|PSK files (*.psk;*.pskx)|*.psk;*.pskx|Men of War files (*.def;*.mdl)|*.def;*.mdl|All files (*.*)|*.*",
            CheckFileExists = true
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }

    private static string? BrowseAnimationFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "PSA animation files (*.psa)|*.psa|All files (*.*)|*.*",
            CheckFileExists = true
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }

    private static string? BrowseConfigFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "GMConverter config (*.ini)|*.ini|All files (*.*)|*.*",
            CheckFileExists = true
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }

    private static string? BrowseOutputFolder()
    {
        return BrowseFolder();
    }

    private static string? BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog();
        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }

    private static string SelectedText(ComboBox comboBox)
    {
        return comboBox.SelectedItem is FormatDisplayItem item
            ? item.Value
            : comboBox.SelectedItem?.ToString() ?? string.Empty;
    }

    private static void EnablePathDrop(TextBox textBox, DirectoryDropBehavior directoryBehavior)
    {
        textBox.AllowDrop = true;
        textBox.DragEnter += (_, args) =>
        {
            args.Effect = TryGetDroppedPath(args.Data, directoryBehavior, out string _) ? DragDropEffects.Copy : DragDropEffects.None;
        };
        textBox.DragDrop += (_, args) =>
        {
            if (TryGetDroppedPath(args.Data, directoryBehavior, out var path))
            {
                textBox.Text = path;
            }
        };
    }

    private static bool TryGetDroppedPath(IDataObject? dataObject, DirectoryDropBehavior directoryBehavior, out string path)
    {
        path = string.Empty;

        if (dataObject?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return false;
        }

        var droppedPath = files[0];

        if (directoryBehavior is DirectoryDropBehavior.Directory && File.Exists(droppedPath))
        {
            droppedPath = Path.GetDirectoryName(droppedPath) ?? droppedPath;
        }

        path = droppedPath;
        return true;
    }

    private void AppendLog(string? line)
    {
        if (line is null)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(line));
            return;
        }

        _logBox.AppendText(line + Environment.NewLine);
    }

    private ILoggerFactory CreateLoggerFactory()
    {
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
            builder.AddProvider(new GuiLoggerProvider(AppendLog));
        });
    }

    private enum DirectoryDropBehavior
    {
        FileOrDirectory,
        Directory
    }

    private sealed record PreviewLoadResult(Model Model, IReadOnlyList<Mesh> PhysicsMeshes);

    private sealed record PreviewStats(
        int MeshCount,
        int SubmeshCount,
        int MaterialCount,
        int TextureCount,
        int VertexCount,
        int TriangleCount,
        int PhysicsPartCount,
        int PhysicsTriangleCount,
        Vector3 Size)
    {
        public static PreviewStats From(Model model, IReadOnlyList<Mesh> physicsMeshes)
        {
            var bounds = model.Bounds();
            return new PreviewStats(
                model.Meshes.Count,
                model.Meshes.Sum(mesh => mesh.Submeshes.Count),
                model.Materials.Count,
                model.Textures.Count,
                model.Meshes.Sum(mesh => mesh.Vertices.Count),
                model.Meshes.Sum(mesh => mesh.Triangles.Count()),
                physicsMeshes.Count,
                physicsMeshes.Sum(mesh => mesh.Triangles.Count()),
                bounds.Max - bounds.Min);
        }

        public override string ToString()
        {
            var meters = Size / SourceUnitsPerMeter;
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Meshes {0} | Submeshes {1} | Materials {2} | Textures {3}\n" +
                "Vertices {4} | Triangles {5} | Physics {6} part(s), {7} tris\n" +
                "Bounds {8:0.##} x {9:0.##} x {10:0.##}u  ({11:0.##} x {12:0.##} x {13:0.##}m)",
                MeshCount,
                SubmeshCount,
                MaterialCount,
                TextureCount,
                VertexCount,
                TriangleCount,
                PhysicsPartCount,
                PhysicsTriangleCount,
                Size.X,
                Size.Y,
                Size.Z,
                meters.X,
                meters.Y,
                meters.Z);
        }
    }

    private sealed record PreviewPhysicsOptions(
        string Mode,
        double CoacdThreshold,
        int MaxConvexPieces,
        int MaxHullVertices);

    private sealed record FormatDisplayItem(string Value, string Label, string Name)
    {
        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Name) ? Label : $"{Label} ({Name})";
        }
    }

    private sealed record GameProfileDisplayItem(string Id, string DisplayName)
    {
        public override string ToString()
        {
            return DisplayName;
        }
    }

    private sealed record GuiConversionSettings(
        string InputFormat,
        string OutputFormat,
        string InputPath,
        string? OutputPath,
        string? BaseName,
        string? ModelPath,
        string? GameDirectory,
        string? EngineDirectory,
        string? MaterialDirectory,
        string? AnimationPath,
        float ScaleFactor,
        ModelAxisMode AxisMode,
        bool BuildMaterials,
        bool GeneratePhysics,
        string? PhysicsMode,
        float PhysicsMass,
        float CoacdThreshold,
        int MaxConvexPieces,
        int MaxHullVertices);

    private sealed class GuiLoggerProvider(Action<string?> appendLog) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new GuiLogger(categoryName, appendLog);
        }

        public void Dispose()
        {
        }
    }

    private sealed class GuiLogger(string categoryName, Action<string?> appendLog) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Warning;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            appendLog($"{logLevel}: {categoryName}: {message}");
            if (exception is not null)
            {
                appendLog(exception.Message);
            }
        }
    }
}
