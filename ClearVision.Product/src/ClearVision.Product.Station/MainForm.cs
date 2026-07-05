using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Runtime;
using ClearVision.Product.Runtime.Abstractions;
using ClearVision.Product.Station.Sync;

namespace ClearVision.Product.Station;

public sealed class MainForm : Form
{
    private readonly RuntimeHost _runtimeHost;
    private readonly StationLocalSettingsStore _settingsStore;
    private readonly StationSiteProfileStore _siteProfileStore;
    private readonly StationSyncSettingsStore _syncSettingsStore;
    private readonly StationHubClient _stationHubClient;
    private readonly StationStudioConnectionTester _studioConnectionTester;
    private readonly StationHardwareSettingsService _hardwareSettingsService;
    private readonly ICameraManager _cameraManager;
    private readonly System.Windows.Forms.Timer _statusRefreshTimer = new();
    private readonly Label _selectedPathLabel = new();
    private readonly Label _stationStatusValueLabel = new();
    private readonly Label _stationStatusDetailLabel = new();
    private readonly Label _cameraStatusValueLabel = new();
    private readonly Label _cameraStatusDetailLabel = new();
    private readonly Label _plcStatusValueLabel = new();
    private readonly Label _plcStatusDetailLabel = new();
    private readonly Label _packageSummaryValueLabel = new();
    private readonly Label _packageSummaryDetailLabel = new();
    private readonly Label _productionValueLabel = new();
    private readonly Label _productionDetailLabel = new();
    private readonly Label _yieldValueLabel = new();
    private readonly Label _yieldDetailLabel = new();
    private readonly Label _statusValueLabel = new();
    private readonly Label _statusDetailLabel = new();
    private readonly Label _timingValueLabel = new();
    private readonly Label _timingDetailLabel = new();
    private readonly Label _statsValueLabel = new();
    private readonly Label _statsDetailLabel = new();
    private readonly Label _taskValueLabel = new();
    private readonly Label _taskDetailLabel = new();
    private readonly RuntimeParameterPanel _runtimeParameterPanel = new();
    private readonly ListView _projectVariableView = new();
    private readonly Button _editProjectVariableButton = new();
    private readonly Button _resetProjectVariableButton = new();
    private readonly TextBox _stationIdTextBox = new();
    private readonly TextBox _lineNameTextBox = new();
    private readonly Button _loadPackageButton = new();
    private readonly Button _loadLastGoodButton = new();
    private readonly Button _chooseImageButton = new();
    private readonly Button _runSingleButton = new();
    private readonly Button _chooseFolderButton = new();
    private readonly Button _runFolderButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _studioConnectionButton = new();
    private readonly Button _hardwareSettingsButton = new();
    private readonly PictureBox _previewBox = new();
    private readonly ListView _recentResultsView = new();
    private readonly TextBox _logTextBox = new();
    private readonly List<RuntimeNormalizedResult> _recentResults = [];
    private readonly List<long> _durations = [];
    private readonly Queue<string> _logLines = new();
    private string? _selectedImagePath;
    private string? _selectedFolderPath;
    private RuntimePackage? _loadedPackage;
    private RuntimeSiteProfile? _activeSiteProfile;
    private const int DefaultLogRetainedLineCount = 200;
    private const int LargeLogRetainedLineCount = 1000;
    private const int MaxLogTrimBatchSize = 1000;
    private const int LeftSidebarWidth = 300;
    private const int RightSidebarWidth = 320;

    private static readonly HashSet<OperatorType> PlcOperatorTypes =
    [
        OperatorType.ModbusCommunication,
        OperatorType.SiemensS7Communication,
        OperatorType.MitsubishiMcCommunication,
        OperatorType.OmronFinsCommunication
    ];

    public MainForm(
        RuntimeHost runtimeHost,
        StationLocalSettingsStore settingsStore,
        StationSiteProfileStore siteProfileStore,
        StationSyncSettingsStore syncSettingsStore,
        StationHubClient stationHubClient,
        StationStudioConnectionTester studioConnectionTester,
        StationHardwareSettingsService hardwareSettingsService,
        ICameraManager cameraManager)
    {
        _runtimeHost = runtimeHost;
        _settingsStore = settingsStore;
        _siteProfileStore = siteProfileStore;
        _syncSettingsStore = syncSettingsStore;
        _stationHubClient = stationHubClient;
        _studioConnectionTester = studioConnectionTester;
        _hardwareSettingsService = hardwareSettingsService;
        _cameraManager = cameraManager;

        Text = "ClearVision 工作站";
        Width = 1280;
        Height = 860;
        MinimumSize = new Size(1024, 720);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(244, 247, 250);

        BuildUi();
        _runtimeParameterPanel.ApplyRequested = ApplyRuntimeParameterProfile;
        _runtimeParameterPanel.ResetRequested = ResetRuntimeParameterProfileToDefault;
        _runtimeParameterPanel.ImportRequested = ImportRuntimeParameterProfile;
        _runtimeParameterPanel.ExportRequested = ExportRuntimeParameterProfile;
        BindRuntimeEvents();
        LoadSettings();
        ApplySnapshot(_runtimeHost.GetSnapshot());
        RefreshHeaderCards();
        UpdateButtonStates();

        _statusRefreshTimer.Interval = 1000;
        _statusRefreshTimer.Tick += (_, _) => RefreshHeaderCards();
        _statusRefreshTimer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _statusRefreshTimer.Stop();
        _statusRefreshTimer.Dispose();
        PersistStationIdentity();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(BuildNavigationBar(), 0, 0);
        root.Controls.Add(BuildDashboardPanel(), 0, 1);
    }

    private Control BuildHeaderPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 0, 8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var row = 0; row < panel.RowCount; row += 1)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        panel.Controls.Add(
            CreateSummaryCard("运行状态", _stationStatusValueLabel, _stationStatusDetailLabel, Color.DodgerBlue),
            0,
            0);
        panel.Controls.Add(
            CreateSummaryCard("工站编号", BuildIdentityContent(), accentColor: Color.SteelBlue),
            1,
            0);
        panel.Controls.Add(
            CreateSummaryCard("相机连接状态", _cameraStatusValueLabel, _cameraStatusDetailLabel, Color.SeaGreen),
            0,
            1);
        panel.Controls.Add(
            CreateSummaryCard("PLC 连接状态", _plcStatusValueLabel, _plcStatusDetailLabel, Color.DarkOrange),
            1,
            1);
        panel.Controls.Add(
            CreateSummaryCard("产量统计", _productionValueLabel, _productionDetailLabel, Color.MediumSlateBlue),
            0,
            2);
        panel.Controls.Add(
            CreateSummaryCard("合格率统计", _yieldValueLabel, _yieldDetailLabel, Color.MediumSeaGreen),
            1,
            2);

        return panel;
    }

    private Control BuildIdentityContent()
    {
        var identityPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0)
        };
        identityPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        identityPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        identityPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        identityPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _stationIdTextBox.Width = 140;
        _lineNameTextBox.Width = 140;

        identityPanel.Controls.Add(new Label { Text = "编号", AutoSize = true, Margin = new Padding(0, 6, 6, 0) }, 0, 0);
        identityPanel.Controls.Add(_stationIdTextBox, 1, 0);
        identityPanel.Controls.Add(new Label { Text = "产线", AutoSize = true, Margin = new Padding(0, 10, 6, 0) }, 0, 1);
        identityPanel.Controls.Add(_lineNameTextBox, 1, 1);

        return identityPanel;
    }

    private static Panel CreateSummaryCard(string title, Label valueLabel, Label detailLabel, Color accentColor)
    {
        ConfigureCardLabel(valueLabel, 18, FontStyle.Bold, accentColor);
        ConfigureCardLabel(detailLabel, 9, FontStyle.Regular, SystemColors.GrayText);
        detailLabel.MaximumSize = new Size(0, 42);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0)
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        content.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            ForeColor = SystemColors.ControlText,
            Margin = new Padding(0, 0, 0, 4)
        }, 0, 0);
        content.Controls.Add(valueLabel, 0, 1);
        content.Controls.Add(detailLabel, 0, 2);

        return CreateSummaryCard(title, content, accentColor);
    }

    private static Panel CreateSummaryCard(string title, Control content, Color accentColor)
    {
        var accent = new Panel
        {
            Dock = DockStyle.Left,
            Width = 4,
            BackColor = accentColor
        };

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 12, 8)
        };
        content.Dock = DockStyle.Fill;
        body.Controls.Add(content);

        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 12, 12),
            Padding = new Padding(0),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            MinimumSize = new Size(0, 76)
        };

        card.Controls.Add(body);
        card.Controls.Add(accent);
        return card;
    }

    private static void ConfigureCardLabel(Label label, float fontSize, FontStyle fontStyle, Color foreColor)
    {
        label.AutoSize = true;
        label.Dock = DockStyle.Top;
        label.Font = new Font(SystemFonts.DefaultFont.FontFamily, fontSize, fontStyle);
        label.ForeColor = foreColor;
        label.Margin = new Padding(0);
    }

    private Control BuildToolbarPanel()
    {
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        ConfigureButton(_loadPackageButton, "加载运行包", async (_, _) => await LoadPackageFromPickerAsync());
        ConfigureButton(_loadLastGoodButton, "加载上次可用包", async (_, _) => await LoadLastGoodPackageAsync());
        ConfigureButton(_chooseImageButton, "选择图片", (_, _) => ChooseImage());
        ConfigureButton(_runSingleButton, "单张运行", async (_, _) => await RunSingleAsync());
        ConfigureButton(_chooseFolderButton, "选择文件夹", (_, _) => ChooseFolder());
        ConfigureButton(_runFolderButton, "批量运行", async (_, _) => await RunFolderAsync());
        ConfigureButton(_stopButton, "停止", async (_, _) => await StopAsync());

        _selectedPathLabel.AutoSize = false;
        _selectedPathLabel.Dock = DockStyle.Fill;
        _selectedPathLabel.Height = 24;
        _selectedPathLabel.AutoEllipsis = true;
        _selectedPathLabel.Margin = new Padding(0, 6, 0, 0);
        _selectedPathLabel.TextAlign = ContentAlignment.MiddleLeft;
        _selectedPathLabel.Text = "当前选择：-";

        buttonPanel.Controls.AddRange(
        [
            _loadPackageButton,
            _loadLastGoodButton,
            _chooseImageButton,
            _runSingleButton,
            _chooseFolderButton,
            _runFolderButton,
            _stopButton
        ]);

        layout.Controls.Add(buttonPanel, 0, 0);
        layout.Controls.Add(_selectedPathLabel, 1, 0);

        return layout;
    }

    private Control BuildMainPanel()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor,
            FixedPanel = FixedPanel.Panel2
        };
        split.SizeChanged += (_, _) => ApplyMainSplitterLayout(split);

        _previewBox.Dock = DockStyle.Fill;
        _previewBox.BackColor = Color.Black;
        _previewBox.SizeMode = PictureBoxSizeMode.Zoom;
        split.Panel1.Padding = new Padding(0, 0, 12, 0);
        split.Panel1.Controls.Add(CreatePaneShell("实时预览", _previewBox));

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = BackColor
        };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));

        var overviewGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 12)
        };
        overviewGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        overviewGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        overviewGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        overviewGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        overviewGrid.Controls.Add(
            CreateSummaryCard("当前判定", _statusValueLabel, _statusDetailLabel, Color.SeaGreen),
            0,
            0);
        overviewGrid.Controls.Add(
            CreateSummaryCard("推理耗时", _timingValueLabel, _timingDetailLabel, Color.DodgerBlue),
            1,
            0);
        overviewGrid.Controls.Add(
            CreateSummaryCard("运行统计", _statsValueLabel, _statsDetailLabel, Color.MediumSlateBlue),
            0,
            1);
        overviewGrid.Controls.Add(
            CreateSummaryCard("当前任务", _taskValueLabel, _taskDetailLabel, Color.SlateGray),
            1,
            1);

        _recentResultsView.Dock = DockStyle.Fill;
        _recentResultsView.View = View.Details;
        _recentResultsView.FullRowSelect = true;
        _recentResultsView.GridLines = true;
        _recentResultsView.Columns.Add("时间", 120);
        _recentResultsView.Columns.Add("图片", 150);
        _recentResultsView.Columns.Add("结果", 80);
        _recentResultsView.Columns.Add("耗时(ms)", 80);
        _recentResultsView.Columns.Add("诊断码", 150);

        right.Controls.Add(overviewGrid, 0, 0);
        right.Controls.Add(CreatePaneShell("最近结果", _recentResultsView), 0, 1);
        split.Panel2.Controls.Add(right);
        ApplyMainSplitterLayout(split);

        return split;
    }

    private static void ApplyMainSplitterLayout(SplitContainer split)
    {
    }

    private Control BuildLogPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Text = "运行日志",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 6)
        }, 0, 0);

        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Multiline = true;
        _logTextBox.ReadOnly = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;
        panel.Controls.Add(_logTextBox, 0, 1);
        return panel;
    }

    private Control BuildNavigationBar()
    {
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        ConfigureButton(_loadPackageButton, "加载运行包", async (_, _) => await LoadPackageFromPickerAsync());
        ConfigureButton(_loadLastGoodButton, "加载上次可用包", async (_, _) => await LoadLastGoodPackageAsync());
        ConfigureButton(_chooseImageButton, "选择图片", (_, _) => ChooseImage());
        ConfigureButton(_runSingleButton, "单张测试", async (_, _) => await RunSingleAsync());
        ConfigureButton(_runFolderButton, "启动系统", async (_, _) => await RunFolderAsync());
        ConfigureButton(_stopButton, "停止", async (_, _) => await StopAsync());
        ConfigureButton(_hardwareSettingsButton, "现场设置", (_, _) => ShowHardwareSettingsDialog());
        ConfigureButton(_studioConnectionButton, "Studio 连接", (_, _) => ShowStudioConnectionDialog());

        _selectedPathLabel.AutoSize = false;
        _selectedPathLabel.Dock = DockStyle.Fill;
        _selectedPathLabel.AutoEllipsis = true;
        _selectedPathLabel.Margin = new Padding(16, 0, 0, 0);
        _selectedPathLabel.TextAlign = ContentAlignment.MiddleRight;
        _selectedPathLabel.ForeColor = Color.DimGray;
        _selectedPathLabel.Text = "当前选择：-";

        buttonPanel.Controls.AddRange(
        [
            _loadPackageButton,
            _loadLastGoodButton,
            _chooseImageButton,
            _runSingleButton,
            _runFolderButton,
            _stopButton,
            _hardwareSettingsButton,
            _studioConnectionButton
        ]);

        layout.Controls.Add(buttonPanel, 0, 0);
        layout.Controls.Add(_selectedPathLabel, 1, 0);

        var shell = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        shell.Controls.Add(layout);
        return shell;
    }

    private Control BuildDashboardPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = BackColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LeftSidebarWidth));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, RightSidebarWidth));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _previewBox.Dock = DockStyle.Fill;
        _previewBox.BackColor = Color.Black;
        _previewBox.SizeMode = PictureBoxSizeMode.Zoom;

        var left = BuildStatusSidebar();
        left.Margin = new Padding(0, 0, 12, 0);

        var center = CreatePaneShell("图像预览", _previewBox);
        center.Margin = new Padding(0, 0, 12, 0);

        var right = BuildMetricsSidebar();

        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(center, 1, 0);
        layout.Controls.Add(right, 2, 0);

        return layout;
    }

    private Control BuildStatusSidebar()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = BackColor,
            AutoScroll = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreateSidebarCard("运行状态", _stationStatusValueLabel, _stationStatusDetailLabel, Color.DodgerBlue), 0, 0);
        layout.Controls.Add(CreateSidebarCard("相机连接状态", _cameraStatusValueLabel, _cameraStatusDetailLabel, Color.SeaGreen), 0, 1);
        layout.Controls.Add(CreateSidebarCard("PLC 连接状态", _plcStatusValueLabel, _plcStatusDetailLabel, Color.DarkOrange), 0, 2);
        layout.Controls.Add(CreateSidebarCard("运行包流程", BuildPackageSummaryContent(), Color.SteelBlue), 0, 3);
        layout.Controls.Add(CreateSidebarCard("现场参数", _runtimeParameterPanel, Color.DarkCyan), 0, 4);

        layout.Controls.Add(CreateSidebarCard("全局变量", BuildProjectVariableMonitor(), Color.MediumPurple), 0, 5);

        return layout;
    }

    private Control BuildMetricsSidebar()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = BackColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));

        var metricsGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 10)
        };
        metricsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metricsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metricsGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        metricsGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        metricsGrid.Controls.Add(CreateSidebarCard("产量统计", _productionValueLabel, _productionDetailLabel, Color.MediumSlateBlue), 0, 0);
        metricsGrid.Controls.Add(CreateSidebarCard("合格率统计", _yieldValueLabel, _yieldDetailLabel, Color.MediumSeaGreen), 1, 0);
        metricsGrid.Controls.Add(CreateSidebarCard("当前判定", _statusValueLabel, _statusDetailLabel, Color.SeaGreen), 0, 1);
        metricsGrid.Controls.Add(CreateSidebarCard("推理耗时", _timingValueLabel, _timingDetailLabel, Color.DodgerBlue), 1, 1);

        var infoStack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 10)
        };
        infoStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        infoStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        infoStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        infoStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        infoStack.Controls.Add(CreateSidebarCard("统计摘要", _statsValueLabel, _statsDetailLabel, Color.MediumPurple), 0, 0);
        infoStack.Controls.Add(CreateSidebarCard("当前任务", _taskValueLabel, _taskDetailLabel, Color.SlateGray), 0, 1);
        infoStack.Controls.Add(CreateSidebarCard("工位信息", BuildIdentityContent(), Color.SteelBlue), 0, 2);

        _recentResultsView.Dock = DockStyle.Fill;
        _recentResultsView.View = View.Details;
        _recentResultsView.FullRowSelect = true;
        _recentResultsView.GridLines = true;
        _recentResultsView.Columns.Clear();
        _recentResultsView.Columns.Add("时间", 56);
        _recentResultsView.Columns.Add("图片", 72);
        _recentResultsView.Columns.Add("结果", 48);
        _recentResultsView.Columns.Add("耗时", 52);
        _recentResultsView.Columns.Add("诊断码", 70);

        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Multiline = true;
        _logTextBox.ReadOnly = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;

        layout.Controls.Add(metricsGrid, 0, 0);
        layout.Controls.Add(infoStack, 0, 1);
        layout.Controls.Add(CreatePaneShell("最近结果", _recentResultsView), 0, 2);
        layout.Controls.Add(CreatePaneShell("运行日志", _logTextBox), 0, 3);

        return layout;
    }

    private Control BuildProjectVariableMonitor()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _projectVariableView.Dock = DockStyle.Fill;
        _projectVariableView.Height = 132;
        _projectVariableView.View = View.Details;
        _projectVariableView.FullRowSelect = true;
        _projectVariableView.GridLines = true;
        _projectVariableView.MultiSelect = false;
        _projectVariableView.Columns.Clear();
        _projectVariableView.Columns.Add("名称", 92);
        _projectVariableView.Columns.Add("当前值", 70);
        _projectVariableView.Columns.Add("来源", 58);
        _projectVariableView.Columns.Add("版本", 42);
        _projectVariableView.Columns.Add("更新", 72);
        _projectVariableView.SelectedIndexChanged += (_, _) => UpdateButtonStates();
        _projectVariableView.DoubleClick += async (_, _) => await EditSelectedProjectVariableAsync();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0)
        };
        ConfigureButton(_editProjectVariableButton, "编辑", async (_, _) => await EditSelectedProjectVariableAsync());
        ConfigureButton(_resetProjectVariableButton, "重置", async (_, _) => await ResetSelectedProjectVariableAsync());
        buttonPanel.Controls.AddRange([_editProjectVariableButton, _resetProjectVariableButton]);

        layout.Controls.Add(_projectVariableView, 0, 0);
        layout.Controls.Add(buttonPanel, 0, 1);
        return layout;
    }

    private void RefreshProjectVariableMonitor()
    {
        _projectVariableView.Items.Clear();
        var package = _loadedPackage;
        if (package == null || package.GlobalVariables.Variables.Count == 0)
        {
            return;
        }

        var definitions = package.GlobalVariables.Variables.ToDictionary(item => item.Id);
        foreach (var snapshot in _runtimeHost.GetProjectVariableSnapshots())
        {
            if (!definitions.TryGetValue(snapshot.VariableId, out var definition))
            {
                continue;
            }

            var item = new ListViewItem(string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.Name : definition.DisplayName);
            item.Tag = definition.Id;
            item.SubItems.Add(FormatProjectVariableValue(snapshot.Value));
            item.SubItems.Add(FormatProjectVariableUpdatedBy(snapshot.UpdatedBy));
            item.SubItems.Add(snapshot.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
            item.SubItems.Add(snapshot.UpdatedAtUtc.ToLocalTime().ToString("HH:mm:ss"));
            _projectVariableView.Items.Add(item);
        }
    }

    private static string FormatProjectVariableValue(System.Text.Json.JsonElement value)
    {
        return ProjectVariableValueConverter.ToObject(value)?.ToString() ?? string.Empty;
    }

    private static string FormatProjectVariableUpdatedBy(ProjectVariableUpdatedBy updatedBy)
    {
        return updatedBy switch
        {
            ProjectVariableUpdatedBy.Initial => "初始值",
            ProjectVariableUpdatedBy.StudioManual => "Studio 写入",
            ProjectVariableUpdatedBy.StationManual => "工站写入",
            ProjectVariableUpdatedBy.OperatorOutput => "算子输出",
            ProjectVariableUpdatedBy.VariableWrite => "变量写入",
            ProjectVariableUpdatedBy.VariableIncrement => "变量递增",
            ProjectVariableUpdatedBy.Reset => "重置",
            _ => updatedBy.ToString()
        };
    }

    private bool TryGetSelectedProjectVariable(out ProjectGlobalVariableDefinition definition, out ProjectVariableValueSnapshot snapshot)
    {
        definition = default!;
        snapshot = default!;
        if (_projectVariableView.SelectedItems.Count == 0 ||
            _projectVariableView.SelectedItems[0].Tag is not Guid variableId ||
            _loadedPackage == null)
        {
            return false;
        }

        var selectedDefinition = _loadedPackage.GlobalVariables.Variables.FirstOrDefault(item => item.Id == variableId);
        var selectedSnapshot = _runtimeHost.GetProjectVariableSnapshots().FirstOrDefault(item => item.VariableId == variableId);
        if (selectedDefinition == null || selectedSnapshot == null)
        {
            return false;
        }

        definition = selectedDefinition;
        snapshot = selectedSnapshot;
        return true;
    }

    private async Task EditSelectedProjectVariableAsync()
    {
        if (!TryGetSelectedProjectVariable(out var definition, out var snapshot))
        {
            return;
        }

        if (!CanEditProjectVariable(definition))
        {
            return;
        }

        var currentText = FormatProjectVariableValue(snapshot.Value);
        var title = $"编辑 {definition.Name}";
        var entered = PromptForProjectVariableValue(title, definition, currentText);
        if (entered == null)
        {
            return;
        }

        if (!TryParseProjectVariableValue(entered, definition.ValueType, out var typedValue, out var error))
        {
            MessageBox.Show(this, error, "值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            await _runtimeHost.SetProjectVariableValueAsync(definition.Id, typedValue, snapshot.Version);
            RefreshProjectVariableMonitor();
            UpdateButtonStates();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "编辑全局变量失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ResetSelectedProjectVariableAsync()
    {
        if (!TryGetSelectedProjectVariable(out var definition, out var snapshot))
        {
            return;
        }

        if (!CanResetProjectVariables() || !definition.ManualWriteAllowed)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"确定将“{definition.Name}”重置为初始值吗？",
            "重置全局变量",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);
        if (result != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _runtimeHost.ResetProjectVariableAsync(definition.Id, snapshot.Version);
            RefreshProjectVariableMonitor();
            UpdateButtonStates();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "重置全局变量失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool CanEditProjectVariable(ProjectGlobalVariableDefinition definition)
    {
        return CanResetProjectVariables() && definition.ManualWriteAllowed;
    }

    private bool CanResetProjectVariables()
    {
        var snapshot = _runtimeHost.GetSnapshot();
        return _loadedPackage != null &&
               snapshot.State is not (RuntimeHostState.Running or RuntimeHostState.Stopping);
    }

    private static bool TryParseProjectVariableValue(
        string text,
        ProjectGlobalVariableValueType valueType,
        out object? value,
        out string error)
    {
        value = null;
        error = string.Empty;
        switch (valueType)
        {
            case ProjectGlobalVariableValueType.String:
                value = text;
                return true;
            case ProjectGlobalVariableValueType.Int64:
                if (long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var longValue))
                {
                    value = longValue;
                    return true;
                }

                error = "Enter a whole number.";
                return false;
            case ProjectGlobalVariableValueType.Double:
                if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var doubleValue))
                {
                    value = doubleValue;
                    return true;
                }

                error = "Enter a number.";
                return false;
            case ProjectGlobalVariableValueType.Boolean:
                if (bool.TryParse(text, out var boolValue))
                {
                    value = boolValue;
                    return true;
                }

                error = "Enter true or false.";
                return false;
            default:
                error = $"Unsupported global variable type '{valueType}'.";
                return false;
        }
    }

    private string? PromptForProjectVariableValue(
        string title,
        ProjectGlobalVariableDefinition definition,
        string currentValue)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(360, 132)
        };

        var label = new Label
        {
            Text = $"{(string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.Name : definition.DisplayName)} ({definition.ValueType})",
            Dock = DockStyle.Top,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 10, 0)
        };
        var textBox = new TextBox
        {
            Dock = DockStyle.Top,
            Text = currentValue,
            Margin = new Padding(10)
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(10)
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 76 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 76 };
        buttons.Controls.AddRange([ok, cancel]);
        dialog.Controls.Add(buttons);
        dialog.Controls.Add(textBox);
        dialog.Controls.Add(label);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        textBox.SelectAll();

        return dialog.ShowDialog(this) == DialogResult.OK ? textBox.Text : null;
    }

    private Control BuildPackageSummaryContent()
    {
        ConfigureCardLabel(_packageSummaryValueLabel, 12, FontStyle.Bold, Color.SteelBlue);
        ConfigureCardLabel(_packageSummaryDetailLabel, 9, FontStyle.Regular, SystemColors.GrayText);
        _packageSummaryValueLabel.MaximumSize = new Size(260, 0);
        _packageSummaryDetailLabel.MaximumSize = new Size(260, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_packageSummaryValueLabel, 0, 0);
        layout.Controls.Add(_packageSummaryDetailLabel, 0, 1);
        return layout;
    }

    private static Panel CreateSidebarCard(string title, Label valueLabel, Label detailLabel, Color accentColor)
    {
        var card = CreateSummaryCard(title, valueLabel, detailLabel, accentColor);
        card.Margin = new Padding(0, 0, 0, 10);
        return card;
    }

    private static Panel CreateSidebarCard(string title, Control content, Color accentColor)
    {
        var card = CreateSummaryCard(title, content, accentColor);
        card.Margin = new Padding(0, 0, 0, 10);
        return card;
    }

    private static Control CreatePaneShell(string title, Control content)
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(10)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        shell.Controls.Add(new Label
        {
            Text = title,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 8)
        }, 0, 0);

        content.Dock = DockStyle.Fill;
        shell.Controls.Add(content, 0, 1);

        var border = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0)
        };
        border.Controls.Add(shell);
        return border;
    }

    private void BindRuntimeEvents()
    {
        _runtimeHost.SnapshotChanged += snapshot => Ui(() =>
        {
            ApplySnapshot(snapshot);
            RefreshHeaderCards();
            UpdateButtonStates();
        });

        _runtimeHost.ResultAvailable += result => Ui(() =>
        {
            _settingsStore.UpdateLastRun(result.RunId);
            ApplyResult(result);
            RefreshHeaderCards();
            UpdateButtonStates();
        });

        _runtimeHost.LogMessage += message => Ui(() => AppendLog(message));
    }

    private void LoadSettings()
    {
        var settings = _settingsStore.Current;
        _stationIdTextBox.Text = settings.StationId ?? string.Empty;
        _lineNameTextBox.Text = settings.LineName ?? string.Empty;

        if (settings.LastUnexpectedExitAtUtc.HasValue)
        {
            AppendLog($"检测到上次未正常退出：{settings.LastUnexpectedExitAtUtc:O}");
        }

        if (!string.IsNullOrWhiteSpace(settings.LastGoodPackagePath))
        {
            AppendLog($"上次可用运行包：{settings.LastGoodPackagePath}");
        }
    }

    private async Task LoadPackageFromPickerAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "请选择运行包目录。可直接选择包含 package.json 的导出包文件夹；如果选择的是上一级目录且其中只有一个运行包，也会自动识别。"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await LoadPackageAsync(dialog.SelectedPath);
    }

    private async Task LoadLastGoodPackageAsync()
    {
        var lastGood = _settingsStore.Current.LastGoodPackagePath;
        if (string.IsNullOrWhiteSpace(lastGood))
        {
            AppendLog("当前还没有记录上次可用的运行包。");
            return;
        }

        await LoadPackageAsync(lastGood);
    }

    private async Task LoadPackageAsync(string packagePath)
    {
        try
        {
            var resolvedPackagePath = ResolvePackageDirectory(packagePath);
            _loadedPackage = await _runtimeHost.LoadPackageAsync(resolvedPackagePath);
            _activeSiteProfile = _siteProfileStore.LoadOrCreate(_loadedPackage);
            _runtimeHost.SetActiveSiteProfile(_activeSiteProfile);
            _runtimeParameterPanel.LoadPackage(_loadedPackage, _activeSiteProfile);
            RefreshProjectVariableMonitor();
            _settingsStore.UpdateLastGoodPackage(resolvedPackagePath);
            _selectedPathLabel.Text = $"当前选择：{resolvedPackagePath}";
            AppendLog($"运行包加载成功：{_loadedPackage.Manifest.PackageName}");
            AppendLog($"现场参数 Profile：Revision {_activeSiteProfile.Revision}，{_activeSiteProfile.Overrides.Count} 项覆盖。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "加载运行包失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"运行包加载失败：{ex.Message}");
        }
        finally
        {
            ApplySnapshot(_runtimeHost.GetSnapshot());
            UpdateButtonStates();
        }
    }

    private void ApplyRuntimeParameterProfile(RuntimeSiteProfile profile)
    {
        if (_loadedPackage == null)
        {
            return;
        }

        try
        {
            _activeSiteProfile = _siteProfileStore.Save(_loadedPackage, profile);
            _runtimeHost.SetActiveSiteProfile(_activeSiteProfile);
            _runtimeParameterPanel.LoadPackage(_loadedPackage, _activeSiteProfile);
            AppendLog($"现场参数已保存：Revision {_activeSiteProfile.Revision}，下次运行生效。");
            UpdateButtonStates();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存现场参数失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"现场参数保存失败：{ex.Message}");
        }
    }

    private void ResetRuntimeParameterProfileToDefault()
    {
        if (_loadedPackage == null)
        {
            return;
        }

        try
        {
            _activeSiteProfile = _siteProfileStore.ResetToPackageDefault(_loadedPackage, _activeSiteProfile);
            _runtimeHost.SetActiveSiteProfile(_activeSiteProfile);
            _runtimeParameterPanel.LoadPackage(_loadedPackage, _activeSiteProfile);
            AppendLog($"现场参数已恢复包默认值：Revision {_activeSiteProfile.Revision}，下次运行生效。");
            UpdateButtonStates();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "恢复现场参数失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"现场参数恢复默认失败：{ex.Message}");
        }
    }

    private void ImportRuntimeParameterProfile()
    {
        if (_loadedPackage == null)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "JSON 文件|*.json|所有文件|*.*",
            Multiselect = false,
            Title = "导入现场参数 Profile"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _activeSiteProfile = _siteProfileStore.ImportFromFile(_loadedPackage, dialog.FileName);
            _runtimeHost.SetActiveSiteProfile(_activeSiteProfile);
            _runtimeParameterPanel.LoadPackage(_loadedPackage, _activeSiteProfile);
            AppendLog($"现场参数已导入：Revision {_activeSiteProfile.Revision}，{_activeSiteProfile.Overrides.Count} 项覆盖，下次运行生效。");
            UpdateButtonStates();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导入现场参数失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"现场参数导入失败：{ex.Message}");
        }
    }

    private void ExportRuntimeParameterProfile(RuntimeSiteProfile profile)
    {
        if (_loadedPackage == null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "JSON 文件|*.json|所有文件|*.*",
            DefaultExt = "json",
            AddExtension = true,
            OverwritePrompt = true,
            Title = "导出现场参数 Profile",
            FileName = _siteProfileStore.GetSuggestedExportFileName(_loadedPackage)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _siteProfileStore.ExportToFile(_loadedPackage, profile, dialog.FileName);
            AppendLog($"现场参数已导出：{dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出现场参数失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"现场参数导出失败：{ex.Message}");
        }
    }

    private void ChooseImage()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "图片文件|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _selectedImagePath = dialog.FileName;
        _selectedFolderPath = null;
        _selectedPathLabel.Text = $"当前选择：{_selectedImagePath}";
        LoadPreviewFromFile(_selectedImagePath);
        UpdateButtonStates();
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "请选择用于批量运行的图片文件夹"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _selectedFolderPath = dialog.SelectedPath;
        _selectedImagePath = null;
        _selectedPathLabel.Text = $"当前选择：{_selectedFolderPath}";
        UpdateButtonStates();
    }

    private async Task RunSingleAsync()
    {
        if (_selectedImagePath == null)
        {
            ChooseImage();
        }

        if (_selectedImagePath == null)
        {
            return;
        }

        try
        {
            await _runtimeHost.RunSingleAsync(_selectedImagePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "单张运行失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"单张运行失败：{ex.Message}");
        }
        finally
        {
            UpdateButtonStates();
        }
    }

    private async Task RunFolderAsync()
    {
        if (_selectedFolderPath == null)
        {
            ChooseFolder();
        }

        if (_selectedFolderPath == null)
        {
            return;
        }

        try
        {
            await _runtimeHost.StartFolderRunAsync(_selectedFolderPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "批量运行失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"批量运行失败：{ex.Message}");
        }
        finally
        {
            UpdateButtonStates();
        }
    }

    private async Task StopAsync()
    {
        var summary = await _runtimeHost.StopAsync();
        AppendLog(
            summary.WasRunning
                ? $"停止完成。待处理={summary.PendingCount}，丢弃={summary.DroppedCount}，超时={summary.TimedOut}"
                : "当前没有正在运行的任务。");
        UpdateButtonStates();
    }

    private void SyncLoadedPackageFromRuntimeHost()
    {
        var hostPackage = _runtimeHost.LoadedPackage;
        if (hostPackage == null)
        {
            if (_loadedPackage != null || _activeSiteProfile != null)
            {
                _loadedPackage = null;
                _activeSiteProfile = null;
                _runtimeParameterPanel.LoadPackage(null, null);
            }

            return;
        }

        var hostProfile = _runtimeHost.ActiveSiteProfile ?? _siteProfileStore.LoadOrCreate(hostPackage);
        var packageChanged = !ReferenceEquals(hostPackage, _loadedPackage);
        var profileChanged = !ProfilesMatch(hostProfile, _activeSiteProfile);
        if (!packageChanged && !profileChanged)
        {
            return;
        }

        _loadedPackage = hostPackage;
        _activeSiteProfile = hostProfile;
        _runtimeParameterPanel.LoadPackage(_loadedPackage, _activeSiteProfile);
    }

    private void ApplySnapshot(RuntimeHostSnapshot snapshot)
    {
        SyncLoadedPackageFromRuntimeHost();

        _statusValueLabel.Text = snapshot.State switch
        {
            RuntimeHostState.Running => "运行中",
            RuntimeHostState.Stopping => "停止中",
            RuntimeHostState.Faulted => "故障",
            RuntimeHostState.Loaded => "就绪",
            _ => "空闲"
        };

        _statusValueLabel.ForeColor = snapshot.State switch
        {
            RuntimeHostState.Running => Color.SeaGreen,
            RuntimeHostState.Stopping => Color.DarkOrange,
            RuntimeHostState.Faulted => Color.Firebrick,
            RuntimeHostState.Loaded => Color.DodgerBlue,
            _ => SystemColors.ControlText
        };

        _statsValueLabel.Text = $"统计：OK {snapshot.SessionOkCount} / NG {snapshot.SessionNgCount} / 异常 {snapshot.SessionErrorCount}";
    }

    private void ApplyResult(RuntimeNormalizedResult result)
    {
        RefreshProjectVariableMonitor();
        _recentResults.Insert(0, result);
        while (_recentResults.Count > (_loadedPackage?.RuntimeProfile.RecentResultsLimit ?? 50))
        {
            _recentResults.RemoveAt(_recentResults.Count - 1);
        }

        _durations.Add(result.ExecutionTimeMs);
        if (_durations.Count > 500)
        {
            _durations.RemoveAt(0);
        }

        var item = new ListViewItem(result.CompletedAtUtc.LocalDateTime.ToString("HH:mm:ss"));
        item.SubItems.Add(Path.GetFileName(result.SourceImagePath) ?? result.ImageId);
        item.SubItems.Add(result.Outcome.ToString().ToUpperInvariant());
        item.SubItems.Add(result.ExecutionTimeMs.ToString());
        item.SubItems.Add(result.DiagnosticCode);
        _recentResultsView.Items.Insert(0, item);
        while (_recentResultsView.Items.Count > (_loadedPackage?.RuntimeProfile.RecentResultsLimit ?? 50))
        {
            _recentResultsView.Items.RemoveAt(_recentResultsView.Items.Count - 1);
        }

        item.SubItems[2].Text = result.Outcome switch
        {
            RuntimeRunOutcome.Ok => "OK",
            RuntimeRunOutcome.Ng => "NG",
            RuntimeRunOutcome.Error => "异常",
            RuntimeRunOutcome.Canceled => "已取消",
            _ => result.Outcome.ToString()
        };

        _timingValueLabel.Text = $"最近推理：{result.ExecutionTimeMs} ms | 平均 {ComputeAverageMs():F1} ms | P95 {ComputeP95Ms():F1} ms";

        var previewBytes = result.OutputImageBytes ?? result.SourceImageBytes;
        if (previewBytes is { Length: > 0 })
        {
            SetPreviewImage(previewBytes);
        }
    }

    private void UpdateButtonStates()
    {
        var snapshot = _runtimeHost.GetSnapshot();
        var hasPackage = _loadedPackage != null;
        var running = snapshot.State is RuntimeHostState.Running or RuntimeHostState.Stopping;

        _loadPackageButton.Enabled = !running;
        _loadLastGoodButton.Enabled = !running && !string.IsNullOrWhiteSpace(_settingsStore.Current.LastGoodPackagePath);
        _chooseImageButton.Enabled = !running;
        _chooseFolderButton.Enabled = !running;
        _runSingleButton.Enabled = hasPackage && !running;
        _runFolderButton.Enabled = hasPackage && !running;
        _stopButton.Enabled = running;
        _hardwareSettingsButton.Enabled = !running;
        _runtimeParameterPanel.SetEditingEnabled(hasPackage && !running);
        var hasSelectedVariable = TryGetSelectedProjectVariable(out var selectedVariable, out _);
        _resetProjectVariableButton.Enabled = hasSelectedVariable && hasPackage && !running && selectedVariable.ManualWriteAllowed;
        _editProjectVariableButton.Enabled = hasSelectedVariable && hasPackage && !running && selectedVariable.ManualWriteAllowed;
    }

    private void AppendLog(string message)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
        var retainedLineCount = Math.Max(0, _loadedPackage?.RuntimeProfile.LogRetainedLineCount ?? DefaultLogRetainedLineCount);
        if (retainedLineCount == 0)
        {
            _logLines.Clear();
            _logTextBox.Clear();
            return;
        }

        _logLines.Enqueue(timestamped);
        if (_logLines.Count > retainedLineCount + GetLogTrimBatchSize(retainedLineCount))
        {
            while (_logLines.Count > retainedLineCount)
            {
                _logLines.Dequeue();
            }

            _logTextBox.Lines = _logLines.ToArray();
        }
        else
        {
            if (_logTextBox.TextLength > 0)
            {
                _logTextBox.AppendText(Environment.NewLine);
            }

            _logTextBox.AppendText(timestamped);
        }

        _logTextBox.SelectionStart = _logTextBox.TextLength;
        _logTextBox.ScrollToCaret();
    }

    private static int GetLogTrimBatchSize(int retainedLineCount)
    {
        if (retainedLineCount < LargeLogRetainedLineCount)
        {
            return 0;
        }

        return Math.Min(MaxLogTrimBatchSize, Math.Max(32, retainedLineCount / 20));
    }

    private void RefreshHeaderCards()
    {
        var snapshot = _runtimeHost.GetSnapshot();
        RefreshStationStatusCard(snapshot);
        RefreshCameraStatusCard();
        RefreshPlcStatusCard();
        RefreshPackageSummaryCard();
        RefreshProductionCards(snapshot);
        RefreshOverviewCards(snapshot);
    }

    private void RefreshStationStatusCard(RuntimeHostSnapshot snapshot)
    {
        _stationStatusValueLabel.Text = snapshot.State switch
        {
            RuntimeHostState.Running => "运行中",
            RuntimeHostState.Stopping => "停止中",
            RuntimeHostState.Faulted => "故障",
            RuntimeHostState.Loaded => "就绪",
            _ => "空闲"
        };
        _stationStatusValueLabel.ForeColor = snapshot.State switch
        {
            RuntimeHostState.Running => Color.SeaGreen,
            RuntimeHostState.Stopping => Color.DarkOrange,
            RuntimeHostState.Faulted => Color.Firebrick,
            RuntimeHostState.Loaded => Color.DodgerBlue,
            _ => Color.DimGray
        };

        var packageName = _loadedPackage?.Manifest.PackageName ?? "未加载";
        _stationStatusDetailLabel.Text = string.IsNullOrWhiteSpace(_selectedImagePath)
            ? $"当前任务：{packageName}"
            : $"当前图片：{Path.GetFileName(_selectedImagePath)}";
    }

    private void RefreshCameraStatusCard()
    {
        if (_loadedPackage?.Flow == null)
        {
            _cameraStatusValueLabel.Text = "未评估";
            _cameraStatusValueLabel.ForeColor = Color.DimGray;
            _cameraStatusDetailLabel.Text = "加载运行包后显示相机模式与连接情况";
            return;
        }

        var imageOp = _loadedPackage.Flow.Operators.FirstOrDefault(op => op.Type == OperatorType.ImageAcquisition);
        if (imageOp == null)
        {
            _cameraStatusValueLabel.Text = "未使用";
            _cameraStatusValueLabel.ForeColor = Color.DimGray;
            _cameraStatusDetailLabel.Text = "当前流程未包含图像采集算子";
            return;
        }

        var sourceType = GetImageAcquisitionSourceType(imageOp);
        if (sourceType.Equals("File", StringComparison.OrdinalIgnoreCase))
        {
            _cameraStatusValueLabel.Text = "文件模式";
            _cameraStatusValueLabel.ForeColor = Color.SteelBlue;
            _cameraStatusDetailLabel.Text = "当前流程使用图片文件输入，不依赖现场相机";
            return;
        }

        var bindings = _cameraManager.GetBindings() ?? [];
        var bindingId = GetParameterString(imageOp, "CameraId", string.Empty);
        var boundCamera = bindings.FirstOrDefault(binding => string.Equals(binding.Id, bindingId, StringComparison.OrdinalIgnoreCase));
        var camera = !string.IsNullOrWhiteSpace(boundCamera?.SerialNumber)
            ? _cameraManager.GetCamera(boundCamera.SerialNumber)
            : null;

        if (camera?.IsConnected == true)
        {
            _cameraStatusValueLabel.Text = "已连接";
            _cameraStatusValueLabel.ForeColor = Color.SeaGreen;
            _cameraStatusDetailLabel.Text = $"绑定：{boundCamera?.DisplayName ?? bindingId}";
            return;
        }

        if (string.IsNullOrWhiteSpace(bindingId))
        {
            _cameraStatusValueLabel.Text = "未配置";
            _cameraStatusValueLabel.ForeColor = Color.DarkOrange;
            _cameraStatusDetailLabel.Text = "流程当前是相机模式，但尚未指定相机绑定";
            return;
        }

        _cameraStatusValueLabel.Text = "未连接";
        _cameraStatusValueLabel.ForeColor = Color.Firebrick;
        _cameraStatusDetailLabel.Text = $"绑定：{boundCamera?.DisplayName ?? bindingId}";
    }

    private void RefreshPlcStatusCard()
    {
        if (_loadedPackage?.Flow == null)
        {
            _plcStatusValueLabel.Text = "未评估";
            _plcStatusValueLabel.ForeColor = Color.DimGray;
            _plcStatusDetailLabel.Text = "加载运行包后显示 PLC 使用情况";
            return;
        }

        var plcOperators = _loadedPackage.Flow.Operators
            .Where(op => PlcOperatorTypes.Contains(op.Type))
            .ToList();

        if (plcOperators.Count == 0)
        {
            _plcStatusValueLabel.Text = "未使用";
            _plcStatusValueLabel.ForeColor = Color.DimGray;
            _plcStatusDetailLabel.Text = "当前流程未包含 PLC 通信算子";
            return;
        }

        var connectionStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in PlcCommunicationOperatorBase.GetConnectionStateSnapshot())
        {
            connectionStates[pair.Key] = pair.Value;
        }

        foreach (var pair in ModbusCommunicationOperator.GetConnectionStateSnapshot())
        {
            connectionStates[pair.Key] = pair.Value;
        }
        if (connectionStates.Count == 0)
        {
            _plcStatusValueLabel.Text = "待连接";
            _plcStatusValueLabel.ForeColor = Color.DarkOrange;
            _plcStatusDetailLabel.Text = $"当前流程包含 {plcOperators.Count} 个 PLC 算子，尚未建立运行时连接";
            return;
        }

        var onlineCount = connectionStates.Count(item => item.Value);
        var offlineCount = connectionStates.Count - onlineCount;
        if (offlineCount == 0)
        {
            _plcStatusValueLabel.Text = "已连接";
            _plcStatusValueLabel.ForeColor = Color.SeaGreen;
            _plcStatusDetailLabel.Text = $"在线 {onlineCount} / 总计 {connectionStates.Count}";
            return;
        }

        _plcStatusValueLabel.Text = "异常";
        _plcStatusValueLabel.ForeColor = Color.Firebrick;
        _plcStatusDetailLabel.Text = $"在线 {onlineCount} / 异常 {offlineCount}";
    }

    private void RefreshPackageSummaryCard()
    {
        if (_loadedPackage?.Flow == null)
        {
            _packageSummaryValueLabel.Text = "未加载运行包";
            _packageSummaryValueLabel.ForeColor = Color.DimGray;
            _packageSummaryDetailLabel.Text = "加载运行包后显示流程名、算子数、连接数、输入模式与 PLC 使用情况。";
            return;
        }

        var flow = _loadedPackage.Flow;
        var imageOp = flow.Operators.FirstOrDefault(op => op.Type == OperatorType.ImageAcquisition);
        var inputMode = imageOp == null
            ? "无图像采集"
            : (GetImageAcquisitionSourceType(imageOp).Equals("File", StringComparison.OrdinalIgnoreCase)
                ? "图片文件"
                : "现场相机");
        var plcCount = flow.Operators.Count(op => PlcOperatorTypes.Contains(op.Type));
        var flowName = string.IsNullOrWhiteSpace(flow.Name) ? "未命名流程" : flow.Name.Trim();
        var flowHash = _loadedPackage.Manifest.FlowHash;
        var shortFlowHash = flowHash[..Math.Min(10, flowHash.Length)];

        _packageSummaryValueLabel.Text = _loadedPackage.Manifest.PackageName;
        _packageSummaryValueLabel.ForeColor = Color.SteelBlue;
        _packageSummaryDetailLabel.Text =
            $"流程：{flowName}{Environment.NewLine}" +
            $"算子 {flow.Operators.Count} | 连线 {flow.Connections.Count}{Environment.NewLine}" +
            $"输入：{inputMode} | PLC：{plcCount}{Environment.NewLine}" +
            $"FlowHash：{shortFlowHash}";
    }

    private void RefreshProductionCards(RuntimeHostSnapshot snapshot)
    {
        var totalCount = snapshot.SessionOkCount + snapshot.SessionNgCount + snapshot.SessionErrorCount;
        _productionValueLabel.Text = totalCount.ToString();
        _productionValueLabel.ForeColor = totalCount > 0 ? Color.MediumSlateBlue : Color.DimGray;
        _productionDetailLabel.Text = $"OK {snapshot.SessionOkCount} / NG {snapshot.SessionNgCount} / 异常 {snapshot.SessionErrorCount}";

        var inspectedCount = snapshot.SessionOkCount + snapshot.SessionNgCount;
        if (inspectedCount <= 0)
        {
            _yieldValueLabel.Text = "--";
            _yieldValueLabel.ForeColor = Color.DimGray;
            _yieldDetailLabel.Text = "当前还没有形成 OK/NG 统计样本";
            return;
        }

        var yieldRate = snapshot.SessionOkCount * 100.0 / inspectedCount;
        _yieldValueLabel.Text = $"{yieldRate:F1}%";
        _yieldValueLabel.ForeColor = yieldRate >= 95 ? Color.SeaGreen : (yieldRate >= 80 ? Color.DarkOrange : Color.Firebrick);
        _yieldDetailLabel.Text = $"按 OK / (OK + NG) 计算，当前有效样本 {inspectedCount}";
    }

    private void RefreshOverviewCards(RuntimeHostSnapshot snapshot)
    {
        _statusValueLabel.Text = _recentResults.FirstOrDefault()?.Outcome switch
        {
            RuntimeRunOutcome.Ok => "OK",
            RuntimeRunOutcome.Ng => "NG",
            RuntimeRunOutcome.Error => "异常",
            RuntimeRunOutcome.Canceled => "已取消",
            _ => snapshot.State switch
            {
                RuntimeHostState.Running => "运行中",
                RuntimeHostState.Stopping => "停止中",
                RuntimeHostState.Faulted => "故障",
                RuntimeHostState.Loaded => "就绪",
                _ => "空闲"
            }
        };
        _statusValueLabel.ForeColor = _statusValueLabel.Text switch
        {
            "OK" => Color.SeaGreen,
            "NG" => Color.DarkOrange,
            "异常" => Color.Firebrick,
            "已取消" => Color.SlateGray,
            "运行中" => Color.DodgerBlue,
            "停止中" => Color.DarkOrange,
            "故障" => Color.Firebrick,
            "就绪" => Color.DodgerBlue,
            _ => Color.DimGray
        };
        _statusDetailLabel.Text = _recentResults.FirstOrDefault() is { } latest
            ? $"诊断码：{latest.DiagnosticCode}"
            : "等待运行结果";

        _timingValueLabel.Text = _recentResults.FirstOrDefault() is { } result
            ? $"{result.ExecutionTimeMs} ms"
            : "--";
        _timingValueLabel.ForeColor = _recentResults.Count > 0 ? Color.DodgerBlue : Color.DimGray;
        _timingDetailLabel.Text = _durations.Count > 0
            ? $"平均 {ComputeAverageMs():F1} ms | P95 {ComputeP95Ms():F1} ms"
            : "等待形成耗时统计";

        var totalCount = snapshot.SessionOkCount + snapshot.SessionNgCount + snapshot.SessionErrorCount;
        _statsValueLabel.Text = totalCount.ToString();
        _statsValueLabel.ForeColor = totalCount > 0 ? Color.MediumSlateBlue : Color.DimGray;
        _statsDetailLabel.Text = $"OK {snapshot.SessionOkCount} / NG {snapshot.SessionNgCount} / 异常 {snapshot.SessionErrorCount}";

        var currentTaskName = _selectedImagePath != null
            ? Path.GetFileName(_selectedImagePath)
            : (_selectedFolderPath != null ? Path.GetFileName(_selectedFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : "未选择");
        _taskValueLabel.Text = currentTaskName;
        _taskValueLabel.ForeColor = (_selectedImagePath != null || _selectedFolderPath != null) ? Color.SlateGray : Color.DimGray;
        var taskSource = _selectedImagePath != null
            ? "单张图片运行"
            : (_selectedFolderPath != null ? "批量文件夹运行" : "请选择图片或文件夹");
        _taskDetailLabel.Text = taskSource;
    }

    private static bool ProfilesMatch(RuntimeSiteProfile? left, RuntimeSiteProfile? right)
    {
        if (left == null || right == null)
        {
            return left == null && right == null;
        }

        return string.Equals(left.ProfileId, right.ProfileId, StringComparison.Ordinal) &&
               string.Equals(left.PackageId, right.PackageId, StringComparison.Ordinal) &&
               string.Equals(left.FlowHash, right.FlowHash, StringComparison.OrdinalIgnoreCase) &&
               left.Revision == right.Revision &&
               left.UpdatedAtUtc == right.UpdatedAtUtc &&
               left.Overrides.Count == right.Overrides.Count &&
               left.Overrides
                   .OrderBy(item => item.ParameterId, StringComparer.Ordinal)
                   .Zip(
                       right.Overrides.OrderBy(item => item.ParameterId, StringComparer.Ordinal),
                       (leftOverride, rightOverride) =>
                           string.Equals(leftOverride.ParameterId, rightOverride.ParameterId, StringComparison.Ordinal) &&
                           string.Equals(leftOverride.Value.GetRawText(), rightOverride.Value.GetRawText(), StringComparison.Ordinal))
                   .All(match => match);
    }

    private static string GetParameterString(ClearVision.Product.Application.DTOs.OperatorDto op, string parameterName, string fallback)
    {
        var parameter = op.Parameters.FirstOrDefault(item => string.Equals(item.Name, parameterName, StringComparison.OrdinalIgnoreCase));
        var value = parameter?.Value ?? parameter?.DefaultValue;
        var text = value?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static string GetImageAcquisitionSourceType(ClearVision.Product.Application.DTOs.OperatorDto op)
    {
        return NormalizeOptionValue(GetParameterString(op, "SourceType", "File"), "File");
    }

    private static string NormalizeOptionValue(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var separatorIndex = normalized.IndexOf('|', StringComparison.Ordinal);
        return separatorIndex >= 0
            ? normalized[..separatorIndex].Trim()
            : normalized;
    }

    private static string ToStateText(RuntimeHostState state)
    {
        return state switch
        {
            RuntimeHostState.Idle => "空闲",
            RuntimeHostState.Loaded => "已加载",
            RuntimeHostState.Running => "运行中",
            RuntimeHostState.Stopping => "停止中",
            RuntimeHostState.Faulted => "故障",
            _ => state.ToString()
        };
    }

    private static string ResolvePackageDirectory(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            throw new InvalidOperationException("未选择运行包目录。");
        }

        var normalized = Path.GetFullPath(selectedPath);
        if (!Directory.Exists(normalized))
        {
            throw new DirectoryNotFoundException($"目录不存在：{normalized}");
        }

        if (File.Exists(Path.Combine(normalized, "package.json")))
        {
            return normalized;
        }

        var candidateDirectories = Directory
            .EnumerateDirectories(normalized)
            .Where(path => File.Exists(Path.Combine(path, "package.json")))
            .ToList();

        if (candidateDirectories.Count == 1)
        {
            return candidateDirectories[0];
        }

        if (candidateDirectories.Count > 1)
        {
            throw new InvalidOperationException("你当前选择的是运行包上一级目录，且里面包含多个运行包。请进入具体的导出包子文件夹后再加载。");
        }

        throw new InvalidOperationException("所选目录不是有效的运行包目录。请确认该目录下包含 package.json。");
    }

    private void PersistStationIdentity()
    {
        _settingsStore.UpdateStationIdentity(_stationIdTextBox.Text, _lineNameTextBox.Text);
    }

    private void ShowStudioConnectionDialog()
    {
        using var dialog = new StudioConnectionDialog(
            _syncSettingsStore,
            _stationHubClient,
            _studioConnectionTester);
        dialog.ShowDialog(this);
    }

    private void ShowHardwareSettingsDialog()
    {
        using var dialog = new HardwareSettingsDialog(_hardwareSettingsService);
        dialog.ShowDialog(this);
        RefreshHeaderCards();
    }

    private void ConfigureButton(Button button, string text, EventHandler onClick)
    {
        button.Text = text;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.Padding = new Padding(10, 5, 10, 5);
        button.Margin = new Padding(0, 0, 8, 0);
        button.FlatStyle = FlatStyle.System;
        button.Click += onClick;
    }

    private void Ui(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed)
                {
                    action();
                }
            });
            return;
        }

        action();
    }

    private void LoadPreviewFromFile(string path)
    {
        try
        {
            SetPreviewImage(File.ReadAllBytes(path));
        }
        catch (Exception ex)
        {
            AppendLog($"预览加载失败：{ex.Message}");
        }
    }

    private void SetPreviewImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = Image.FromStream(stream);
        var previous = _previewBox.Image;
        _previewBox.Image = (Image)image.Clone();
        previous?.Dispose();
    }

    private double ComputeAverageMs()
    {
        return _durations.Count == 0 ? 0 : _durations.Average();
    }

    private double ComputeP95Ms()
    {
        if (_durations.Count == 0)
        {
            return 0;
        }

        var ordered = _durations.OrderBy(value => value).ToArray();
        var index = (int)Math.Ceiling(ordered.Length * 0.95) - 1;
        index = Math.Clamp(index, 0, ordered.Length - 1);
        return ordered[index];
    }
}
