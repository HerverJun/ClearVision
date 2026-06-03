using ClearVision.Product.Station.Sync;

namespace ClearVision.Product.Station;

public sealed class StudioConnectionDialog : Form
{
    private readonly StationSyncSettingsStore _settingsStore;
    private readonly StationHubClient _hubClient;
    private readonly StationStudioConnectionTester _connectionTester;
    private readonly System.Windows.Forms.Timer _statusTimer = new();
    private readonly CheckBox _enabledCheckBox = new();
    private readonly TextBox _studioBaseUrlTextBox = new();
    private readonly TextBox _sharedTokenTextBox = new();
    private readonly Button _toggleTokenButton = new();
    private readonly Button _testConnectionButton = new();
    private readonly Button _saveAndConnectButton = new();
    private readonly Button _disableSyncButton = new();
    private readonly Label _connectionStatusLabel = new();
    private readonly Label _targetHubLabel = new();
    private readonly Label _lastErrorLabel = new();
    private readonly Label _settingsPathLabel = new();
    private readonly Label _operationMessageLabel = new();

    public StudioConnectionDialog(
        StationSyncSettingsStore settingsStore,
        StationHubClient hubClient,
        StationStudioConnectionTester connectionTester)
    {
        _settingsStore = settingsStore;
        _hubClient = hubClient;
        _connectionTester = connectionTester;

        Text = "Studio 连接";
        Width = 640;
        Height = 520;
        MinimumSize = new Size(560, 460);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.White;

        BuildUi();
        LoadSettings();
        RefreshConnectionStatus();

        _statusTimer.Interval = 1000;
        _statusTimer.Tick += (_, _) => RefreshConnectionStatus();
        _statusTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _statusTimer.Stop();
        _statusTimer.Dispose();
        base.OnFormClosed(e);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(16),
            BackColor = Color.White
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "Studio 地址应填写 A 电脑地址，例如 http://192.168.137.13:5000，不要填写 localhost。",
            AutoSize = true,
            MaximumSize = new Size(580, 0),
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 12)
        }, 0, 0);

        root.Controls.Add(BuildInputPanel(), 0, 1);
        root.Controls.Add(BuildStatusPanel(), 0, 2);

        _operationMessageLabel.AutoSize = true;
        _operationMessageLabel.MaximumSize = new Size(580, 0);
        _operationMessageLabel.ForeColor = Color.DimGray;
        _operationMessageLabel.Margin = new Padding(0, 8, 0, 8);
        root.Controls.Add(_operationMessageLabel, 0, 3);

        root.Controls.Add(BuildButtonPanel(), 0, 4);
    }

    private Control BuildInputPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 16)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _enabledCheckBox.Text = "启用同步";
        _enabledCheckBox.AutoSize = true;
        _enabledCheckBox.Margin = new Padding(0, 0, 0, 10);
        panel.Controls.Add(_enabledCheckBox, 1, 0);

        panel.Controls.Add(CreateFieldLabel("Studio 地址"), 0, 1);
        _studioBaseUrlTextBox.Dock = DockStyle.Fill;
        _studioBaseUrlTextBox.Margin = new Padding(0, 0, 0, 10);
        _studioBaseUrlTextBox.TextChanged += (_, _) => RefreshTargetHub();
        panel.Controls.Add(_studioBaseUrlTextBox, 1, 1);

        panel.Controls.Add(CreateFieldLabel("共享 token"), 0, 2);
        panel.Controls.Add(BuildTokenInput(), 1, 2);

        return panel;
    }

    private Control BuildTokenInput()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _sharedTokenTextBox.Dock = DockStyle.Fill;
        _sharedTokenTextBox.UseSystemPasswordChar = true;
        _sharedTokenTextBox.Margin = new Padding(0, 0, 8, 0);

        ConfigureButton(_toggleTokenButton, "显示", (_, _) => ToggleTokenVisibility());
        layout.Controls.Add(_sharedTokenTextBox, 0, 0);
        layout.Controls.Add(_toggleTokenButton, 1, 0);
        return layout;
    }

    private Control BuildStatusPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = Color.FromArgb(248, 250, 252),
            Padding = new Padding(12),
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i += 1)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        AddInfoRow(panel, 0, "当前状态", _connectionStatusLabel);
        AddInfoRow(panel, 1, "目标 Hub", _targetHubLabel);
        AddInfoRow(panel, 2, "最近错误", _lastErrorLabel);
        AddInfoRow(panel, 3, "配置文件", _settingsPathLabel);
        return panel;
    }

    private Control BuildButtonPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0)
        };

        var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
        ConfigureButton(cancelButton, "取消", (_, _) => Close());
        ConfigureButton(_disableSyncButton, "关闭同步", (_, _) => DisableSync());
        ConfigureButton(_saveAndConnectButton, "保存并连接", (_, _) => SaveAndConnect());
        ConfigureButton(_testConnectionButton, "测试连接", async (_, _) => await TestConnectionAsync());

        panel.Controls.Add(cancelButton);
        panel.Controls.Add(_disableSyncButton);
        panel.Controls.Add(_saveAndConnectButton);
        panel.Controls.Add(_testConnectionButton);
        return panel;
    }

    private void LoadSettings()
    {
        var settings = _settingsStore.Current;
        _enabledCheckBox.Checked = settings.Enabled;
        _studioBaseUrlTextBox.Text = settings.StudioBaseUrl;
        _sharedTokenTextBox.Text = settings.SharedToken;
        _settingsPathLabel.Text = settings.SettingsPath;
        RefreshTargetHub();
    }

    private async Task TestConnectionAsync()
    {
        var settings = BuildSettings(enabled: true);
        if (!ValidateSettings(settings, requireToken: true))
        {
            return;
        }

        SetBusy(true);
        _operationMessageLabel.Text = "正在测试 Studio /health 和 SignalR 入口...";
        try
        {
            var result = await _connectionTester.TestAsync(settings);
            _operationMessageLabel.Text = result.Message;
            MessageBox.Show(
                this,
                result.Message,
                result.Success ? "测试成功" : "测试失败",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
            RefreshConnectionStatus();
        }
    }

    private void SaveAndConnect()
    {
        var settings = BuildSettings(enabled: true);
        if (!ValidateSettings(settings, requireToken: true))
        {
            return;
        }

        _enabledCheckBox.Checked = true;
        var saved = _settingsStore.SaveConnectionSettings(settings);
        _operationMessageLabel.Text = "已保存配置，正在通知后台同步服务重新连接。";
        _targetHubLabel.Text = saved.ResolvedStudioHubUrl;
        RefreshConnectionStatus();
    }

    private void DisableSync()
    {
        var settings = BuildSettings(enabled: false);
        _enabledCheckBox.Checked = false;
        _settingsStore.SaveConnectionSettings(settings);
        _operationMessageLabel.Text = "已关闭同步，Station 将停止向 Studio 上报。";
        RefreshConnectionStatus();
    }

    private StationSyncConnectionSettings BuildSettings(bool enabled)
    {
        var baseUrl = _studioBaseUrlTextBox.Text.Trim();
        return new StationSyncConnectionSettings
        {
            Enabled = enabled,
            StudioBaseUrl = baseUrl,
            StudioHubUrl = StationSyncOptions.ResolveStudioHubUrl(baseUrl, string.Empty),
            SharedToken = _sharedTokenTextBox.Text,
            SettingsPath = _settingsStore.SettingsPath
        };
    }

    private bool ValidateSettings(StationSyncConnectionSettings settings, bool requireToken)
    {
        if (string.IsNullOrWhiteSpace(settings.StudioBaseUrl))
        {
            MessageBox.Show(this, "请填写 Studio 地址。", "Studio 连接", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (!Uri.TryCreate(settings.StudioBaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show(this, "Studio 地址必须是 http 或 https 开头的完整地址。", "Studio 连接", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (requireToken && string.IsNullOrWhiteSpace(settings.SharedToken))
        {
            MessageBox.Show(this, "请填写共享 token。", "Studio 连接", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private void RefreshConnectionStatus()
    {
        _connectionStatusLabel.Text = _hubClient.IsConnected
            ? $"已连接 ({_hubClient.ConnectionState})"
            : $"未连接 ({_hubClient.ConnectionState})";
        _connectionStatusLabel.ForeColor = _hubClient.IsConnected ? Color.SeaGreen : Color.DimGray;
        _lastErrorLabel.Text = string.IsNullOrWhiteSpace(_hubClient.LastErrorMessage)
            ? "-"
            : _hubClient.LastErrorMessage;
        RefreshTargetHub();
    }

    private void RefreshTargetHub()
    {
        var baseUrl = _studioBaseUrlTextBox.Text.Trim();
        var hubUrl = StationSyncOptions.ResolveStudioHubUrl(baseUrl, string.Empty);
        _targetHubLabel.Text = string.IsNullOrWhiteSpace(hubUrl) ? "-" : hubUrl;
    }

    private void ToggleTokenVisibility()
    {
        _sharedTokenTextBox.UseSystemPasswordChar = !_sharedTokenTextBox.UseSystemPasswordChar;
        _toggleTokenButton.Text = _sharedTokenTextBox.UseSystemPasswordChar ? "显示" : "隐藏";
    }

    private void SetBusy(bool busy)
    {
        _testConnectionButton.Enabled = !busy;
        _saveAndConnectButton.Enabled = !busy;
        _disableSyncButton.Enabled = !busy;
        _toggleTokenButton.Enabled = !busy;
    }

    private static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 8, 10)
        };
    }

    private static void AddInfoRow(TableLayoutPanel panel, int row, string title, Label valueLabel)
    {
        panel.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 0, 8, 8)
        }, 0, row);

        valueLabel.AutoSize = true;
        valueLabel.MaximumSize = new Size(420, 0);
        valueLabel.Margin = new Padding(0, 0, 0, 8);
        panel.Controls.Add(valueLabel, 1, row);
    }

    private static void ConfigureButton(Button button, string text, EventHandler onClick)
    {
        button.Text = text;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.Padding = new Padding(10, 5, 10, 5);
        button.Margin = new Padding(8, 0, 0, 0);
        button.FlatStyle = FlatStyle.System;
        button.Click += onClick;
    }
}
