using System.ComponentModel;
using Acme.Product.Core.Cameras;
using Acme.Product.Core.Entities;

namespace Acme.Product.Station;

public sealed class HardwareSettingsDialog : Form
{
    private readonly StationHardwareSettingsService _settingsService;
    private readonly List<CameraBindingConfig> _cameraBindings = [];
    private CommunicationConfig _communication = new();
    private string _activeCameraId = string.Empty;
    private int _selectedCameraIndex = -1;
    private bool _loadingCameraForm;
    private bool _loadingPlcForm;
    private BindingList<PlcAddressMapping> _plcMappings = [];

    private readonly ListBox _cameraList = new();
    private readonly TextBox _cameraIdTextBox = new();
    private readonly TextBox _cameraNameTextBox = new();
    private readonly TextBox _cameraSerialTextBox = new();
    private readonly TextBox _cameraManufacturerTextBox = new();
    private readonly NumericUpDown _cameraExposureInput = new();
    private readonly NumericUpDown _cameraGainInput = new();
    private readonly ComboBox _cameraPixelFormatCombo = new();
    private readonly ComboBox _cameraTriggerModeCombo = new();
    private readonly ComboBox _cameraHardwareTriggerSourceCombo = new();
    private readonly ComboBox _cameraSoftwareTriggerSourceCombo = new();
    private readonly NumericUpDown _cameraTargetFrameRateInput = new();
    private readonly NumericUpDown _enterDebounceInput = new();
    private readonly NumericUpDown _enterTimeoutInput = new();
    private readonly TextBox _enterDeviceTextBox = new();
    private readonly CheckBox _ignoreEnterBusyCheckBox = new();
    private readonly TextBox _serialPortTextBox = new();
    private readonly NumericUpDown _serialBaudInput = new();
    private readonly NumericUpDown _serialDebounceInput = new();
    private readonly NumericUpDown _serialTimeoutInput = new();
    private readonly CheckBox _ignoreSerialBusyCheckBox = new();
    private readonly CheckBox _cameraEnabledCheckBox = new();
    private readonly Label _cameraStatusLabel = new();

    private readonly ComboBox _plcProtocolCombo = new();
    private readonly TextBox _plcIpTextBox = new();
    private readonly NumericUpDown _plcPortInput = new();
    private readonly ComboBox _s7CpuTypeCombo = new();
    private readonly NumericUpDown _s7RackInput = new();
    private readonly NumericUpDown _s7SlotInput = new();
    private readonly NumericUpDown _plcHeartbeatInput = new();
    private readonly DataGridView _plcMappingsGrid = new();
    private readonly Label _plcStatusLabel = new();

    public HardwareSettingsDialog(StationHardwareSettingsService settingsService)
    {
        _settingsService = settingsService;

        Text = "现场硬件设置";
        Width = 900;
        Height = 680;
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(244, 247, 250);

        Controls.Add(BuildLayout());
        Load += async (_, _) => await LoadSettingsAsync();
    }

    private Control BuildLayout()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(12, 6)
        };

        tabs.TabPages.Add(new TabPage("相机配置") { Controls = { BuildCameraTab() } });
        tabs.TabPages.Add(new TabPage("PLC 配置") { Controls = { BuildPlcTab() } });
        return tabs;
    }

    private Control BuildCameraTab()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12),
            BackColor = BackColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _cameraList.Dock = DockStyle.Fill;
        _cameraList.SelectedIndexChanged += (_, _) => SelectCamera(_cameraList.SelectedIndex);

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(_cameraList, 0, 0);

        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        leftButtons.Controls.Add(CreateButton("搜索并添加", async (_, _) => await DiscoverAndAddCamerasAsync()));
        leftButtons.Controls.Add(CreateButton("新增", (_, _) => AddCamera()));
        leftButtons.Controls.Add(CreateButton("删除", (_, _) => RemoveSelectedCamera()));
        left.Controls.Add(leftButtons, 0, 1);

        _cameraStatusLabel.AutoSize = false;
        _cameraStatusLabel.Height = 42;
        _cameraStatusLabel.Dock = DockStyle.Fill;
        _cameraStatusLabel.ForeColor = Color.DimGray;
        _cameraStatusLabel.Text = "相机配置保存在 Station 本地。";
        left.Controls.Add(_cameraStatusLabel, 0, 2);

        ConfigureNumeric(_cameraExposureInput, 10, 1_000_000, 5000, 1);
        ConfigureNumeric(_cameraGainInput, 0, 24, 1, 2);
        _cameraPixelFormatCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _cameraPixelFormatCombo.Items.AddRange(["Mono8", "BGR8", "RGB8", "BayerRG8", "BayerGB8", "BayerGR8", "BayerBG8", "Auto"]);
        _cameraTriggerModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _cameraTriggerModeCombo.Items.AddRange(["Software", "External", "Continuous"]);
        _cameraTriggerModeCombo.SelectedIndexChanged += (_, _) => SyncCameraTriggerControls();
        _cameraHardwareTriggerSourceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _cameraHardwareTriggerSourceCombo.Items.AddRange(["Line0", "Line1", "Line2", "Line3"]);
        _cameraSoftwareTriggerSourceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _cameraSoftwareTriggerSourceCombo.Items.AddRange(["Manual", "EnterPhotoelectric", "SerialPhotoelectric"]);
        _cameraSoftwareTriggerSourceCombo.SelectedIndexChanged += (_, _) => SyncCameraTriggerControls();
        ConfigureNumeric(_cameraTargetFrameRateInput, CameraTriggerModeExtensions.MinTargetFrameRateFps, CameraTriggerModeExtensions.MaxTargetFrameRateFps, CameraTriggerModeExtensions.DefaultTargetFrameRateFps, 0);
        ConfigureNumeric(_enterDebounceInput, CameraSoftwareTriggerSourceExtensions.MinEnterPhotoelectricDebounceMs, CameraSoftwareTriggerSourceExtensions.MaxEnterPhotoelectricDebounceMs, CameraSoftwareTriggerSourceExtensions.DefaultEnterPhotoelectricDebounceMs, 0);
        ConfigureNumeric(_enterTimeoutInput, CameraSoftwareTriggerSourceExtensions.MinEnterPhotoelectricTimeoutMs, CameraSoftwareTriggerSourceExtensions.MaxEnterPhotoelectricTimeoutMs, CameraSoftwareTriggerSourceExtensions.DefaultEnterPhotoelectricTimeoutMs, 0);
        ConfigureNumeric(_serialBaudInput, 1, 1_000_000, CameraSoftwareTriggerSourceExtensions.DefaultSerialPhotoelectricBaudRate, 0);
        ConfigureNumeric(_serialDebounceInput, CameraSoftwareTriggerSourceExtensions.MinEnterPhotoelectricDebounceMs, CameraSoftwareTriggerSourceExtensions.MaxEnterPhotoelectricDebounceMs, CameraSoftwareTriggerSourceExtensions.DefaultEnterPhotoelectricDebounceMs, 0);
        ConfigureNumeric(_serialTimeoutInput, CameraSoftwareTriggerSourceExtensions.MinEnterPhotoelectricTimeoutMs, CameraSoftwareTriggerSourceExtensions.MaxEnterPhotoelectricTimeoutMs, CameraSoftwareTriggerSourceExtensions.DefaultEnterPhotoelectricTimeoutMs, 0);
        _ignoreEnterBusyCheckBox.Text = "忙碌时忽略回车光电触发";
        _ignoreEnterBusyCheckBox.Checked = true;
        _ignoreSerialBusyCheckBox.Text = "忙碌时忽略串口光电触发";
        _ignoreSerialBusyCheckBox.Checked = true;
        _cameraEnabledCheckBox.Text = "启用";
        _cameraEnabledCheckBox.Checked = true;

        var form = CreateFormGrid();
        AddFormRow(form, "绑定 ID", _cameraIdTextBox);
        AddFormRow(form, "显示名称", _cameraNameTextBox);
        AddFormRow(form, "序列号", _cameraSerialTextBox);
        AddFormRow(form, "厂商", _cameraManufacturerTextBox);
        AddFormRow(form, "曝光(us)", _cameraExposureInput);
        AddFormRow(form, "增益(dB)", _cameraGainInput);
        AddFormRow(form, "像素格式", _cameraPixelFormatCombo);
        AddFormRow(form, "触发模式", _cameraTriggerModeCombo);
        AddFormRow(form, "外触发输入线", _cameraHardwareTriggerSourceCombo);
        AddFormRow(form, "软件触发来源", _cameraSoftwareTriggerSourceCombo);
        AddFormRow(form, "采集帧率(fps)", _cameraTargetFrameRateInput);
        AddFormRow(form, "回车防抖(ms)", _enterDebounceInput);
        AddFormRow(form, "回车超时(ms)", _enterTimeoutInput);
        AddFormRow(form, "回车设备过滤", _enterDeviceTextBox);
        AddFormRow(form, "回车防重复", _ignoreEnterBusyCheckBox);
        AddFormRow(form, "串口号", _serialPortTextBox);
        AddFormRow(form, "串口波特率", _serialBaudInput);
        AddFormRow(form, "串口防抖(ms)", _serialDebounceInput);
        AddFormRow(form, "串口超时(ms)", _serialTimeoutInput);
        AddFormRow(form, "串口防重复", _ignoreSerialBusyCheckBox);
        AddFormRow(form, "启用状态", _cameraEnabledCheckBox);

        var actionRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actionRow.Controls.Add(CreateButton("设为活动相机", (_, _) => SetSelectedCameraActive()));
        actionRow.Controls.Add(CreateButton("测试连接", async (_, _) => await TestSelectedCameraAsync()));
        actionRow.Controls.Add(CreateButton("保存相机配置", async (_, _) => await SaveCameraSettingsAsync()));
        var actionRowIndex = form.RowCount++;
        form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        form.Controls.Add(actionRow, 1, actionRowIndex);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(form, 1, 0);
        return root;
    }

    private Control BuildPlcTab()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _plcProtocolCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _plcProtocolCombo.Items.AddRange(["S7", "MC", "FINS"]);
        _plcProtocolCombo.SelectedIndexChanged += (_, _) => ChangePlcProtocol();
        ConfigureNumeric(_plcPortInput, 1, 65535, 102, 0);
        ConfigureNumeric(_s7RackInput, 0, 15, 0, 0);
        ConfigureNumeric(_s7SlotInput, 0, 15, 1, 0);
        ConfigureNumeric(_plcHeartbeatInput, 100, 60000, 1000, 0);

        _s7CpuTypeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _s7CpuTypeCombo.Items.AddRange(["S7-1200", "S7-1500", "S7-300", "S7-400", "S7-200", "S7-200 Smart"]);

        var connectionForm = CreateFormGrid();
        connectionForm.ColumnStyles[1].Width = 100;
        AddFormRow(connectionForm, "协议", _plcProtocolCombo);
        AddFormRow(connectionForm, "PLC IP", _plcIpTextBox);
        AddFormRow(connectionForm, "端口", _plcPortInput);
        AddFormRow(connectionForm, "CPU", _s7CpuTypeCombo);
        AddFormRow(connectionForm, "Rack", _s7RackInput);
        AddFormRow(connectionForm, "Slot", _s7SlotInput);
        AddFormRow(connectionForm, "心跳(ms)", _plcHeartbeatInput);
        root.Controls.Add(connectionForm, 0, 0);

        ConfigurePlcMappingsGrid();
        root.Controls.Add(_plcMappingsGrid, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };
        actions.Controls.Add(CreateButton("添加变量", (_, _) => AddPlcMapping()));
        actions.Controls.Add(CreateButton("删除变量", (_, _) => DeleteSelectedPlcMapping()));
        actions.Controls.Add(CreateButton("添加常用地址", (_, _) => AddDefaultPlcMappings()));
        actions.Controls.Add(CreateButton("测试连接", async (_, _) => await TestPlcConnectionAsync()));
        actions.Controls.Add(CreateButton("保存 PLC 配置", async (_, _) => await SavePlcSettingsAsync()));

        _plcStatusLabel.AutoSize = false;
        _plcStatusLabel.Width = 320;
        _plcStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _plcStatusLabel.ForeColor = Color.DimGray;
        actions.Controls.Add(_plcStatusLabel);
        root.Controls.Add(actions, 0, 2);

        return root;
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var snapshot = await _settingsService.LoadAsync();
            _cameraBindings.Clear();
            _cameraBindings.AddRange(snapshot.Cameras);
            _activeCameraId = snapshot.ActiveCameraId;
            _communication = snapshot.Communication;

            RefreshCameraList();
            LoadPlcForm();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"加载现场硬件设置失败：{ex.Message}", "现场硬件设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshCameraList()
    {
        _loadingCameraForm = true;
        try
        {
            _cameraList.Items.Clear();
            for (var index = 0; index < _cameraBindings.Count; index += 1)
            {
                var binding = _cameraBindings[index];
                var activeSuffix = binding.Id.Equals(_activeCameraId, StringComparison.OrdinalIgnoreCase) ? "  [活动]" : string.Empty;
                _cameraList.Items.Add($"{binding.DisplayName} ({binding.Id}){activeSuffix}");
            }

            _selectedCameraIndex = _cameraBindings.Count > 0 ? Math.Clamp(_selectedCameraIndex, 0, _cameraBindings.Count - 1) : -1;
            _cameraList.SelectedIndex = _selectedCameraIndex;
            LoadCameraForm(_selectedCameraIndex);
        }
        finally
        {
            _loadingCameraForm = false;
        }
    }

    private void SelectCamera(int nextIndex)
    {
        if (_loadingCameraForm)
        {
            return;
        }

        CommitCameraForm(_selectedCameraIndex);
        _selectedCameraIndex = nextIndex;
        LoadCameraForm(nextIndex);
    }

    private void LoadCameraForm(int index)
    {
        _loadingCameraForm = true;
        try
        {
            var hasCamera = index >= 0 && index < _cameraBindings.Count;
            SetCameraEditorEnabled(hasCamera);
            if (!hasCamera)
            {
                _cameraIdTextBox.Text = string.Empty;
                _cameraNameTextBox.Text = string.Empty;
                _cameraSerialTextBox.Text = string.Empty;
                _cameraManufacturerTextBox.Text = string.Empty;
                _cameraExposureInput.Value = 5000;
                _cameraGainInput.Value = 1;
                _cameraPixelFormatCombo.SelectedItem = "Mono8";
                _cameraTriggerModeCombo.SelectedItem = "Software";
                _cameraHardwareTriggerSourceCombo.SelectedItem = "Line0";
                _cameraSoftwareTriggerSourceCombo.SelectedItem = "Manual";
                _cameraTargetFrameRateInput.Value = CameraTriggerModeExtensions.DefaultTargetFrameRateFps;
                _enterDebounceInput.Value = CameraSoftwareTriggerSourceExtensions.DefaultEnterPhotoelectricDebounceMs;
                _enterTimeoutInput.Value = CameraSoftwareTriggerSourceExtensions.DefaultEnterPhotoelectricTimeoutMs;
                _enterDeviceTextBox.Text = string.Empty;
                _ignoreEnterBusyCheckBox.Checked = true;
                _serialPortTextBox.Text = string.Empty;
                _serialBaudInput.Value = CameraSoftwareTriggerSourceExtensions.DefaultSerialPhotoelectricBaudRate;
                _serialDebounceInput.Value = CameraSoftwareTriggerSourceExtensions.DefaultEnterPhotoelectricDebounceMs;
                _serialTimeoutInput.Value = CameraSoftwareTriggerSourceExtensions.DefaultEnterPhotoelectricTimeoutMs;
                _ignoreSerialBusyCheckBox.Checked = true;
                _cameraEnabledCheckBox.Checked = true;
                SyncCameraTriggerControls();
                return;
            }

            var binding = _cameraBindings[index];
            binding.Normalize();
            _cameraIdTextBox.Text = binding.Id;
            _cameraNameTextBox.Text = binding.DisplayName;
            _cameraSerialTextBox.Text = binding.SerialNumber;
            _cameraManufacturerTextBox.Text = binding.Manufacturer;
            SetNumericValue(_cameraExposureInput, binding.ExposureTimeUs);
            SetNumericValue(_cameraGainInput, binding.GainDb);
            SelectComboValue(_cameraPixelFormatCombo, binding.PixelFormat);
            SelectComboValue(_cameraTriggerModeCombo, CameraTriggerModeExtensions.Normalize(binding.TriggerMode).ToConfigValue());
            SelectComboValue(_cameraHardwareTriggerSourceCombo, CameraHardwareTriggerSourceExtensions.Normalize(binding.HardwareTriggerSource));
            SelectComboValue(_cameraSoftwareTriggerSourceCombo, CameraSoftwareTriggerSourceExtensions.Normalize(binding.SoftwareTriggerSource).ToConfigValue());
            SetNumericValue(_cameraTargetFrameRateInput, CameraTriggerModeExtensions.NormalizeTargetFrameRate(binding.TargetFrameRateFps));
            SetNumericValue(_enterDebounceInput, binding.EnterPhotoelectricDebounceMs);
            SetNumericValue(_enterTimeoutInput, binding.EnterPhotoelectricTimeoutMs);
            _enterDeviceTextBox.Text = binding.EnterPhotoelectricDeviceId;
            _ignoreEnterBusyCheckBox.Checked = binding.IgnoreEnterTriggerWhileBusy;
            _serialPortTextBox.Text = binding.SerialPhotoelectricPortName;
            SetNumericValue(_serialBaudInput, binding.SerialPhotoelectricBaudRate);
            SetNumericValue(_serialDebounceInput, binding.SerialPhotoelectricDebounceMs);
            SetNumericValue(_serialTimeoutInput, binding.SerialPhotoelectricTimeoutMs);
            _ignoreSerialBusyCheckBox.Checked = binding.IgnoreSerialPhotoelectricTriggerWhileBusy;
            _cameraEnabledCheckBox.Checked = binding.IsEnabled;
            SyncCameraTriggerControls();
        }
        finally
        {
            _loadingCameraForm = false;
        }
    }

    private void CommitCameraForm(int index)
    {
        if (_loadingCameraForm || index < 0 || index >= _cameraBindings.Count)
        {
            return;
        }

        var binding = _cameraBindings[index];
        binding.Id = string.IsNullOrWhiteSpace(_cameraIdTextBox.Text) ? binding.Id : _cameraIdTextBox.Text.Trim();
        binding.DisplayName = _cameraNameTextBox.Text.Trim();
        binding.SerialNumber = _cameraSerialTextBox.Text.Trim();
        binding.Manufacturer = _cameraManufacturerTextBox.Text.Trim();
        binding.ExposureTimeUs = (double)_cameraExposureInput.Value;
        binding.GainDb = (double)_cameraGainInput.Value;
        binding.PixelFormat = _cameraPixelFormatCombo.Text;
        binding.TriggerMode = CameraTriggerModeExtensions.Normalize(_cameraTriggerModeCombo.Text).ToConfigValue();
        binding.HardwareTriggerSource = CameraHardwareTriggerSourceExtensions.Normalize(_cameraHardwareTriggerSourceCombo.Text);
        binding.SoftwareTriggerSource = CameraSoftwareTriggerSourceExtensions.Normalize(_cameraSoftwareTriggerSourceCombo.Text).ToConfigValue();
        binding.TargetFrameRateFps = (int)_cameraTargetFrameRateInput.Value;
        binding.EnterPhotoelectricDebounceMs = (int)_enterDebounceInput.Value;
        binding.EnterPhotoelectricTimeoutMs = (int)_enterTimeoutInput.Value;
        binding.EnterPhotoelectricDeviceId = _enterDeviceTextBox.Text.Trim();
        binding.IgnoreEnterTriggerWhileBusy = _ignoreEnterBusyCheckBox.Checked;
        binding.SerialPhotoelectricPortName = _serialPortTextBox.Text.Trim();
        binding.SerialPhotoelectricBaudRate = (int)_serialBaudInput.Value;
        binding.SerialPhotoelectricDebounceMs = (int)_serialDebounceInput.Value;
        binding.SerialPhotoelectricTimeoutMs = (int)_serialTimeoutInput.Value;
        binding.IgnoreSerialPhotoelectricTriggerWhileBusy = _ignoreSerialBusyCheckBox.Checked;
        binding.IsEnabled = _cameraEnabledCheckBox.Checked;
        binding.Normalize();
    }

    private void SetCameraEditorEnabled(bool enabled)
    {
        foreach (var control in new Control[]
        {
            _cameraIdTextBox,
            _cameraNameTextBox,
            _cameraSerialTextBox,
            _cameraManufacturerTextBox,
            _cameraExposureInput,
            _cameraGainInput,
            _cameraPixelFormatCombo,
            _cameraTriggerModeCombo,
            _cameraHardwareTriggerSourceCombo,
            _cameraSoftwareTriggerSourceCombo,
            _cameraTargetFrameRateInput,
            _enterDebounceInput,
            _enterTimeoutInput,
            _enterDeviceTextBox,
            _ignoreEnterBusyCheckBox,
            _serialPortTextBox,
            _serialBaudInput,
            _serialDebounceInput,
            _serialTimeoutInput,
            _ignoreSerialBusyCheckBox,
            _cameraEnabledCheckBox
        })
        {
            control.Enabled = enabled;
        }

        SyncCameraTriggerControls();
    }

    private void SyncCameraTriggerControls()
    {
        var hasCamera = _selectedCameraIndex >= 0 && _selectedCameraIndex < _cameraBindings.Count;
        if (!hasCamera)
        {
            foreach (var control in new Control[]
            {
                _cameraHardwareTriggerSourceCombo,
                _cameraSoftwareTriggerSourceCombo,
                _cameraTargetFrameRateInput,
                _enterDebounceInput,
                _enterTimeoutInput,
                _enterDeviceTextBox,
                _ignoreEnterBusyCheckBox,
                _serialPortTextBox,
                _serialBaudInput,
                _serialDebounceInput,
                _serialTimeoutInput,
                _ignoreSerialBusyCheckBox
            })
            {
                control.Enabled = false;
            }

            return;
        }

        var triggerMode = CameraTriggerModeExtensions.Normalize(_cameraTriggerModeCombo.Text);
        var softwareSource = CameraSoftwareTriggerSourceExtensions.Normalize(_cameraSoftwareTriggerSourceCombo.Text);
        var isSoftware = triggerMode == CameraTriggerMode.Software;
        var isExternal = triggerMode == CameraTriggerMode.External;
        var isEnterPhotoelectric = isSoftware && softwareSource == CameraSoftwareTriggerSource.EnterPhotoelectric;
        var isSerialPhotoelectric = isSoftware && softwareSource == CameraSoftwareTriggerSource.SerialPhotoelectric;

        _cameraHardwareTriggerSourceCombo.Enabled = isExternal;
        _cameraSoftwareTriggerSourceCombo.Enabled = isSoftware;
        _cameraTargetFrameRateInput.Enabled = !isSoftware;
        _enterDebounceInput.Enabled = isEnterPhotoelectric;
        _enterTimeoutInput.Enabled = isEnterPhotoelectric;
        _enterDeviceTextBox.Enabled = isEnterPhotoelectric;
        _ignoreEnterBusyCheckBox.Enabled = isEnterPhotoelectric;
        _serialPortTextBox.Enabled = isSerialPhotoelectric;
        _serialBaudInput.Enabled = isSerialPhotoelectric;
        _serialDebounceInput.Enabled = isSerialPhotoelectric;
        _serialTimeoutInput.Enabled = isSerialPhotoelectric;
        _ignoreSerialBusyCheckBox.Enabled = isSerialPhotoelectric;
    }

    private async Task DiscoverAndAddCamerasAsync()
    {
        CommitCameraForm(_selectedCameraIndex);
        var devices = (await _settingsService.DiscoverCamerasAsync()).ToList();
        var added = 0;
        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.CameraId) ||
                _cameraBindings.Any(binding => binding.SerialNumber.Equals(device.CameraId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _cameraBindings.Add(new CameraBindingConfig
            {
                Id = BuildCameraBindingId(device),
                DisplayName = string.IsNullOrWhiteSpace(device.Name) ? device.CameraId : device.Name,
                SerialNumber = device.CameraId,
                IpAddress = device.IpAddress ?? string.Empty,
                Manufacturer = string.IsNullOrWhiteSpace(device.Manufacturer) ? "Huaray" : device.Manufacturer,
                ModelName = device.Model ?? string.Empty,
                InterfaceType = device.ConnectionType ?? string.Empty,
                IsEnabled = true
            });
            added += 1;
        }

        if (string.IsNullOrWhiteSpace(_activeCameraId) && _cameraBindings.Count > 0)
        {
            _activeCameraId = _cameraBindings[0].Id;
        }

        RefreshCameraList();
        _cameraStatusLabel.Text = added > 0 ? $"已添加 {added} 台在线相机。" : "未发现新的在线相机。";
    }

    private void AddCamera()
    {
        CommitCameraForm(_selectedCameraIndex);
        var binding = new CameraBindingConfig
        {
            Id = $"cam-{Guid.NewGuid():N}"[..12],
            DisplayName = "Camera",
            IsEnabled = true
        };
        binding.Normalize();
        _cameraBindings.Add(binding);
        if (string.IsNullOrWhiteSpace(_activeCameraId))
        {
            _activeCameraId = binding.Id;
        }

        _selectedCameraIndex = _cameraBindings.Count - 1;
        RefreshCameraList();
    }

    private void RemoveSelectedCamera()
    {
        if (_selectedCameraIndex < 0 || _selectedCameraIndex >= _cameraBindings.Count)
        {
            return;
        }

        var binding = _cameraBindings[_selectedCameraIndex];
        if (MessageBox.Show(this, $"删除相机绑定“{binding.DisplayName}”？", "删除相机", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        _cameraBindings.RemoveAt(_selectedCameraIndex);
        if (binding.Id.Equals(_activeCameraId, StringComparison.OrdinalIgnoreCase))
        {
            _activeCameraId = _cameraBindings.FirstOrDefault(item => item.IsEnabled)?.Id
                ?? _cameraBindings.FirstOrDefault()?.Id
                ?? string.Empty;
        }

        _selectedCameraIndex = Math.Min(_selectedCameraIndex, _cameraBindings.Count - 1);
        RefreshCameraList();
    }

    private void SetSelectedCameraActive()
    {
        CommitCameraForm(_selectedCameraIndex);
        if (_selectedCameraIndex < 0 || _selectedCameraIndex >= _cameraBindings.Count)
        {
            return;
        }

        _activeCameraId = _cameraBindings[_selectedCameraIndex].Id;
        RefreshCameraList();
    }

    private async Task TestSelectedCameraAsync()
    {
        CommitCameraForm(_selectedCameraIndex);
        if (_selectedCameraIndex < 0 || _selectedCameraIndex >= _cameraBindings.Count)
        {
            return;
        }

        _cameraStatusLabel.Text = "正在测试相机连接...";
        var result = await _settingsService.TestCameraAsync(_cameraBindings[_selectedCameraIndex]);
        _cameraStatusLabel.ForeColor = result.Success ? Color.SeaGreen : Color.Firebrick;
        _cameraStatusLabel.Text = result.Message;
    }

    private async Task SaveCameraSettingsAsync()
    {
        CommitCameraForm(_selectedCameraIndex);
        try
        {
            var snapshot = await _settingsService.SaveCameraBindingsAsync(_cameraBindings, _activeCameraId);
            _cameraBindings.Clear();
            _cameraBindings.AddRange(snapshot.Cameras);
            _activeCameraId = snapshot.ActiveCameraId;
            _cameraStatusLabel.ForeColor = Color.SeaGreen;
            _cameraStatusLabel.Text = "相机配置已保存并应用到当前 Station。";
            RefreshCameraList();
        }
        catch (Exception ex)
        {
            _cameraStatusLabel.ForeColor = Color.Firebrick;
            _cameraStatusLabel.Text = $"保存失败：{ex.Message}";
        }
    }

    private void LoadPlcForm()
    {
        _loadingPlcForm = true;
        try
        {
            _communication.Normalize();
            SelectComboValue(_plcProtocolCombo, _communication.ActiveProtocol);
            LoadActivePlcProfile();
        }
        finally
        {
            _loadingPlcForm = false;
        }
    }

    private void ChangePlcProtocol()
    {
        if (_loadingPlcForm)
        {
            return;
        }

        CommitActivePlcProfile();
        _communication.ActiveProtocol = CommunicationConfig.NormalizeProtocolKey(_plcProtocolCombo.Text);
        LoadActivePlcProfile();
    }

    private void LoadActivePlcProfile()
    {
        _loadingPlcForm = true;
        try
        {
            _communication.Normalize();
            var protocol = CommunicationConfig.NormalizeProtocolKey(_communication.ActiveProtocol);
            var profile = _communication.GetProfile(protocol);
            _plcIpTextBox.Text = profile.IpAddress;
            SetNumericValue(_plcPortInput, profile.Port > 0 ? profile.Port : CommunicationConfig.GetDefaultPort(protocol));
            SetNumericValue(_plcHeartbeatInput, _communication.HeartbeatIntervalMs);

            var isS7 = protocol == CommunicationConfig.ProtocolS7;
            _s7CpuTypeCombo.Enabled = isS7;
            _s7RackInput.Enabled = isS7;
            _s7SlotInput.Enabled = isS7;
            if (profile is S7CommunicationProfile s7)
            {
                SelectComboValue(_s7CpuTypeCombo, s7.CpuType);
                SetNumericValue(_s7RackInput, s7.Rack);
                SetNumericValue(_s7SlotInput, s7.Slot);
            }
            else
            {
                SelectComboValue(_s7CpuTypeCombo, "S7-1200");
                SetNumericValue(_s7RackInput, 0);
                SetNumericValue(_s7SlotInput, 1);
            }

            _plcMappings = new BindingList<PlcAddressMapping>(
                profile.Mappings.Select(mapping => mapping.Normalize()).ToList());
            _plcMappingsGrid.DataSource = _plcMappings;
            _plcStatusLabel.Text = $"当前协议：{protocol}";
            _plcStatusLabel.ForeColor = Color.DimGray;
        }
        finally
        {
            _loadingPlcForm = false;
        }
    }

    private void CommitActivePlcProfile()
    {
        _plcMappingsGrid.EndEdit();
        var protocol = CommunicationConfig.NormalizeProtocolKey(_communication.ActiveProtocol);
        var profile = _communication.GetProfile(protocol);
        profile.IpAddress = _plcIpTextBox.Text.Trim();
        profile.Port = (int)_plcPortInput.Value;
        profile.Mappings = _plcMappings.Select(mapping => mapping.Normalize()).Where(mapping => !mapping.IsEmpty()).ToList();

        if (profile is S7CommunicationProfile s7)
        {
            s7.CpuType = string.IsNullOrWhiteSpace(_s7CpuTypeCombo.Text) ? "S7-1200" : _s7CpuTypeCombo.Text.Trim();
            s7.Rack = (int)_s7RackInput.Value;
            s7.Slot = (int)_s7SlotInput.Value;
        }

        _communication.HeartbeatIntervalMs = (int)_plcHeartbeatInput.Value;
        _communication.Normalize();
    }

    private void ConfigurePlcMappingsGrid()
    {
        _plcMappingsGrid.Dock = DockStyle.Fill;
        _plcMappingsGrid.AutoGenerateColumns = false;
        _plcMappingsGrid.AllowUserToAddRows = false;
        _plcMappingsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _plcMappingsGrid.MultiSelect = false;
        _plcMappingsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PlcAddressMapping.Name), HeaderText = "变量名", Width = 120 });
        _plcMappingsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PlcAddressMapping.Address), HeaderText = "地址", Width = 120 });
        _plcMappingsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PlcAddressMapping.DataType), HeaderText = "数据类型", Width = 90 });
        _plcMappingsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PlcAddressMapping.Description), HeaderText = "说明", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _plcMappingsGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(PlcAddressMapping.CanWrite), HeaderText = "可写", Width = 60 });
    }

    private void AddPlcMapping()
    {
        _plcMappings.Add(new PlcAddressMapping { DataType = "Bool" });
    }

    private void DeleteSelectedPlcMapping()
    {
        if (_plcMappingsGrid.CurrentRow?.DataBoundItem is PlcAddressMapping mapping)
        {
            _plcMappings.Remove(mapping);
        }
    }

    private void AddDefaultPlcMappings()
    {
        var protocol = CommunicationConfig.NormalizeProtocolKey(_communication.ActiveProtocol);
        foreach (var mapping in BuildDefaultMappings(protocol))
        {
            if (_plcMappings.Any(item => item.Name.Equals(mapping.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _plcMappings.Add(mapping);
        }
    }

    private async Task TestPlcConnectionAsync()
    {
        CommitActivePlcProfile();
        _plcStatusLabel.ForeColor = Color.DimGray;
        _plcStatusLabel.Text = "正在测试 PLC 连接...";
        var result = await _settingsService.TestPlcConnectionAsync(_communication, CancellationToken.None);
        _plcStatusLabel.ForeColor = result.Success ? Color.SeaGreen : Color.Firebrick;
        _plcStatusLabel.Text = result.Message;
    }

    private async Task SavePlcSettingsAsync()
    {
        CommitActivePlcProfile();
        try
        {
            var snapshot = await _settingsService.SavePlcSettingsAsync(_communication);
            _communication = snapshot.Communication;
            _plcStatusLabel.ForeColor = Color.SeaGreen;
            _plcStatusLabel.Text = "PLC 配置已保存，旧连接状态已清理。";
            LoadPlcForm();
        }
        catch (Exception ex)
        {
            _plcStatusLabel.ForeColor = Color.Firebrick;
            _plcStatusLabel.Text = $"保存失败：{ex.Message}";
        }
    }

    private static IEnumerable<PlcAddressMapping> BuildDefaultMappings(string protocol)
    {
        if (CommunicationConfig.NormalizeProtocolKey(protocol) == CommunicationConfig.ProtocolS7)
        {
            return
            [
                new PlcAddressMapping { Name = "Trigger", Address = "DB1.DBX100.0", DataType = "Bool", Description = "PLC 触发信号" },
                new PlcAddressMapping { Name = "Result", Address = "DB1.DBW102", DataType = "Int16", Description = "视觉结果写回", CanWrite = true },
                new PlcAddressMapping { Name = "Heartbeat", Address = "DB1.DBX104.0", DataType = "Bool", Description = "视觉心跳", CanWrite = true },
                new PlcAddressMapping { Name = "Busy", Address = "DB1.DBX104.1", DataType = "Bool", Description = "视觉忙碌状态", CanWrite = true },
                new PlcAddressMapping { Name = "Done", Address = "DB1.DBX104.2", DataType = "Bool", Description = "检测完成", CanWrite = true },
                new PlcAddressMapping { Name = "ErrorCode", Address = "DB1.DBW106", DataType = "Int16", Description = "异常码", CanWrite = true }
            ];
        }

        var prefix = CommunicationConfig.NormalizeProtocolKey(protocol) switch
        {
            CommunicationConfig.ProtocolFins => "DM",
            _ => "D"
        };

        static string Address(string prefix, int offset) => $"{prefix}{offset}";

        return
        [
            new PlcAddressMapping { Name = "Trigger", Address = Address(prefix, 100), DataType = "Bool", Description = "PLC 触发信号" },
            new PlcAddressMapping { Name = "Result", Address = Address(prefix, 101), DataType = "Int16", Description = "视觉结果写回", CanWrite = true },
            new PlcAddressMapping { Name = "Heartbeat", Address = Address(prefix, 102), DataType = "Bool", Description = "视觉心跳", CanWrite = true },
            new PlcAddressMapping { Name = "Busy", Address = Address(prefix, 103), DataType = "Bool", Description = "视觉忙碌状态", CanWrite = true },
            new PlcAddressMapping { Name = "Done", Address = Address(prefix, 104), DataType = "Bool", Description = "检测完成", CanWrite = true },
            new PlcAddressMapping { Name = "ErrorCode", Address = Address(prefix, 105), DataType = "Int16", Description = "异常码", CanWrite = true }
        ];
    }

    private static string BuildCameraBindingId(CameraInfo device)
    {
        var seed = string.IsNullOrWhiteSpace(device.CameraId) ? Guid.NewGuid().ToString("N") : device.CameraId;
        var safe = new string(seed.Where(char.IsLetterOrDigit).Take(8).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? $"cam-{Guid.NewGuid():N}"[..12] : $"cam-{safe}";
    }

    private static TableLayoutPanel CreateFormGrid()
    {
        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            Padding = new Padding(16, 0, 0, 0)
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return form;
    }

    private static void AddFormRow(TableLayoutPanel form, string label, Control input)
    {
        var row = form.RowCount++;
        form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        form.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 8, 8, 8)
        }, 0, row);
        input.Dock = DockStyle.Top;
        input.Margin = new Padding(0, 4, 0, 4);
        form.Controls.Add(input, 1, row);
    }

    private static Button CreateButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 5, 10, 5),
            Margin = new Padding(0, 0, 8, 8),
            FlatStyle = FlatStyle.System
        };
        button.Click += handler;
        return button;
    }

    private static void ConfigureNumeric(NumericUpDown input, decimal min, decimal max, decimal value, int decimals)
    {
        input.Minimum = min;
        input.Maximum = max;
        input.DecimalPlaces = decimals;
        input.Value = Math.Clamp(value, min, max);
        input.ThousandsSeparator = true;
    }

    private static void SetNumericValue(NumericUpDown input, double value)
    {
        SetNumericValue(input, (decimal)value);
    }

    private static void SetNumericValue(NumericUpDown input, int value)
    {
        SetNumericValue(input, (decimal)value);
    }

    private static void SetNumericValue(NumericUpDown input, decimal value)
    {
        input.Value = Math.Clamp(value, input.Minimum, input.Maximum);
    }

    private static void SelectComboValue(ComboBox combo, string value)
    {
        var index = combo.Items
            .Cast<object>()
            .Select((item, itemIndex) => new { Text = item.ToString() ?? string.Empty, Index = itemIndex })
            .FirstOrDefault(item => item.Text.Equals(value, StringComparison.OrdinalIgnoreCase))
            ?.Index ?? -1;

        if (index >= 0)
        {
            combo.SelectedIndex = index;
        }
        else if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }
}
