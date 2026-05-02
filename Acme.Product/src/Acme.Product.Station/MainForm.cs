using Acme.Product.Runtime;
using Acme.Product.Runtime.Abstractions;

namespace Acme.Product.Station;

public sealed class MainForm : Form
{
    private readonly RuntimeHost _runtimeHost;
    private readonly StationLocalSettingsStore _settingsStore;
    private readonly Label _packageLabel = new();
    private readonly Label _stateLabel = new();
    private readonly Label _hashLabel = new();
    private readonly Label _selectedPathLabel = new();
    private readonly Label _statusValueLabel = new();
    private readonly Label _timingValueLabel = new();
    private readonly Label _statsValueLabel = new();
    private readonly TextBox _stationIdTextBox = new();
    private readonly TextBox _lineNameTextBox = new();
    private readonly Button _loadPackageButton = new();
    private readonly Button _loadLastGoodButton = new();
    private readonly Button _chooseImageButton = new();
    private readonly Button _runSingleButton = new();
    private readonly Button _chooseFolderButton = new();
    private readonly Button _runFolderButton = new();
    private readonly Button _stopButton = new();
    private readonly PictureBox _previewBox = new();
    private readonly ListView _recentResultsView = new();
    private readonly TextBox _logTextBox = new();
    private readonly List<RuntimeNormalizedResult> _recentResults = [];
    private readonly List<long> _durations = [];
    private string? _selectedImagePath;
    private string? _selectedFolderPath;
    private RuntimePackage? _loadedPackage;

    public MainForm(RuntimeHost runtimeHost, StationLocalSettingsStore settingsStore)
    {
        _runtimeHost = runtimeHost;
        _settingsStore = settingsStore;

        Text = "ClearVision Station";
        Width = 1280;
        Height = 860;
        MinimumSize = new Size(1024, 720);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        BindRuntimeEvents();
        LoadSettings();
        ApplySnapshot(_runtimeHost.GetSnapshot());
        UpdateButtonStates();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        PersistStationIdentity();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        Controls.Add(root);

        root.Controls.Add(BuildHeaderPanel(), 0, 0);
        root.Controls.Add(BuildToolbarPanel(), 0, 1);
        root.Controls.Add(BuildMainPanel(), 0, 2);
        root.Controls.Add(BuildLogPanel(), 0, 3);
    }

    private Control BuildHeaderPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));

        _packageLabel.AutoEllipsis = true;
        _packageLabel.Text = "Package: -";
        _stateLabel.Text = "State: Idle";
        _hashLabel.AutoEllipsis = true;
        _hashLabel.Text = "FlowHash: -";

        var identityPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false
        };
        _stationIdTextBox.Width = 110;
        _lineNameTextBox.Width = 110;
        identityPanel.Controls.Add(new Label { Text = "StationId", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        identityPanel.Controls.Add(_stationIdTextBox);
        identityPanel.Controls.Add(new Label { Text = "Line", AutoSize = true, Margin = new Padding(12, 6, 6, 0) });
        identityPanel.Controls.Add(_lineNameTextBox);

        panel.Controls.Add(_packageLabel, 0, 0);
        panel.Controls.Add(_stateLabel, 1, 0);
        panel.Controls.Add(_hashLabel, 2, 0);
        panel.Controls.Add(identityPanel, 3, 0);
        return panel;
    }

    private Control BuildToolbarPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 12)
        };

        ConfigureButton(_loadPackageButton, "Load Package", async (_, _) => await LoadPackageFromPickerAsync());
        ConfigureButton(_loadLastGoodButton, "Load Last Good", async (_, _) => await LoadLastGoodPackageAsync());
        ConfigureButton(_chooseImageButton, "Choose Image", (_, _) => ChooseImage());
        ConfigureButton(_runSingleButton, "Run Single", async (_, _) => await RunSingleAsync());
        ConfigureButton(_chooseFolderButton, "Choose Folder", (_, _) => ChooseFolder());
        ConfigureButton(_runFolderButton, "Run Folder", async (_, _) => await RunFolderAsync());
        ConfigureButton(_stopButton, "Stop", async (_, _) => await StopAsync());

        _selectedPathLabel.AutoSize = true;
        _selectedPathLabel.Margin = new Padding(12, 10, 0, 0);
        _selectedPathLabel.Text = "Selected: -";

        panel.Controls.AddRange(
        [
            _loadPackageButton,
            _loadLastGoodButton,
            _chooseImageButton,
            _runSingleButton,
            _chooseFolderButton,
            _runFolderButton,
            _stopButton,
            _selectedPathLabel
        ]);

        return panel;
    }

    private Control BuildMainPanel()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 520
        };

        _previewBox.Dock = DockStyle.Fill;
        _previewBox.BackColor = Color.Black;
        _previewBox.SizeMode = PictureBoxSizeMode.Zoom;
        split.Panel1.Controls.Add(_previewBox);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5
        };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _statusValueLabel.Font = new Font(Font.FontFamily, 24, FontStyle.Bold);
        _statusValueLabel.Text = "IDLE";
        _statusValueLabel.AutoSize = true;
        _timingValueLabel.Text = "Last run: -";
        _timingValueLabel.AutoSize = true;
        _statsValueLabel.Text = "Stats: -";
        _statsValueLabel.AutoSize = true;

        var title = new Label
        {
            Text = "Recent Results",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Padding = new Padding(0, 12, 0, 6)
        };

        _recentResultsView.Dock = DockStyle.Fill;
        _recentResultsView.View = View.Details;
        _recentResultsView.FullRowSelect = true;
        _recentResultsView.Columns.Add("Time", 120);
        _recentResultsView.Columns.Add("Image", 180);
        _recentResultsView.Columns.Add("Outcome", 80);
        _recentResultsView.Columns.Add("Ms", 70);
        _recentResultsView.Columns.Add("Code", 180);

        right.Controls.Add(_statusValueLabel, 0, 0);
        right.Controls.Add(_timingValueLabel, 0, 1);
        right.Controls.Add(_statsValueLabel, 0, 2);
        right.Controls.Add(title, 0, 3);
        right.Controls.Add(_recentResultsView, 0, 4);
        split.Panel2.Controls.Add(right);

        return split;
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
            Text = "Log",
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

    private void BindRuntimeEvents()
    {
        _runtimeHost.SnapshotChanged += snapshot => Ui(() =>
        {
            ApplySnapshot(snapshot);
            UpdateButtonStates();
        });

        _runtimeHost.ResultAvailable += result => Ui(() =>
        {
            _settingsStore.UpdateLastRun(result.RunId);
            ApplyResult(result);
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
            AppendLog($"Previous session did not exit cleanly ({settings.LastUnexpectedExitAtUtc:O}).");
        }

        if (!string.IsNullOrWhiteSpace(settings.LastGoodPackagePath))
        {
            AppendLog($"Last good package: {settings.LastGoodPackagePath}");
        }
    }

    private async Task LoadPackageFromPickerAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a runtime package folder"
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
            AppendLog("No last-good package is recorded yet.");
            return;
        }

        await LoadPackageAsync(lastGood);
    }

    private async Task LoadPackageAsync(string packagePath)
    {
        try
        {
            _loadedPackage = await _runtimeHost.LoadPackageAsync(packagePath);
            _settingsStore.UpdateLastGoodPackage(packagePath);
            AppendLog($"Package loaded: {_loadedPackage.Manifest.PackageName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load Package Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"Load package failed: {ex.Message}");
        }
        finally
        {
            ApplySnapshot(_runtimeHost.GetSnapshot());
            UpdateButtonStates();
        }
    }

    private void ChooseImage()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Image Files|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _selectedImagePath = dialog.FileName;
        _selectedFolderPath = null;
        _selectedPathLabel.Text = $"Selected: {_selectedImagePath}";
        LoadPreviewFromFile(_selectedImagePath);
        UpdateButtonStates();
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select an image replay folder"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _selectedFolderPath = dialog.SelectedPath;
        _selectedImagePath = null;
        _selectedPathLabel.Text = $"Selected: {_selectedFolderPath}";
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
            MessageBox.Show(this, ex.Message, "Run Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"Run failed: {ex.Message}");
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
            MessageBox.Show(this, ex.Message, "Replay Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"Replay failed: {ex.Message}");
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
                ? $"Stop completed. Pending={summary.PendingCount}, Dropped={summary.DroppedCount}, TimedOut={summary.TimedOut}"
                : "Nothing was running.");
        UpdateButtonStates();
    }

    private void ApplySnapshot(RuntimeHostSnapshot snapshot)
    {
        _packageLabel.Text = $"Package: {snapshot.PackageName ?? "-"}";
        _stateLabel.Text = $"State: {snapshot.State}";
        _hashLabel.Text = $"FlowHash: {snapshot.FlowHash ?? "-"}";

        _statusValueLabel.Text = snapshot.State switch
        {
            RuntimeHostState.Running => "RUNNING",
            RuntimeHostState.Stopping => "STOPPING",
            RuntimeHostState.Faulted => "FAULTED",
            RuntimeHostState.Loaded => "READY",
            _ => "IDLE"
        };

        _statusValueLabel.ForeColor = snapshot.State switch
        {
            RuntimeHostState.Running => Color.SeaGreen,
            RuntimeHostState.Stopping => Color.DarkOrange,
            RuntimeHostState.Faulted => Color.Firebrick,
            RuntimeHostState.Loaded => Color.DodgerBlue,
            _ => SystemColors.ControlText
        };

        _statsValueLabel.Text = $"Stats: OK {snapshot.SessionOkCount} / NG {snapshot.SessionNgCount} / ERR {snapshot.SessionErrorCount}";
    }

    private void ApplyResult(RuntimeNormalizedResult result)
    {
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

        _timingValueLabel.Text = $"Last run: {result.ExecutionTimeMs} ms | Avg {ComputeAverageMs():F1} ms | P95 {ComputeP95Ms():F1} ms";

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
    }

    private void AppendLog(string message)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
        var lines = _logTextBox.Lines.ToList();
        lines.Add(timestamped);
        while (lines.Count > (_loadedPackage?.RuntimeProfile.LogRetainedLineCount ?? 200))
        {
            lines.RemoveAt(0);
        }

        _logTextBox.Lines = lines.ToArray();
        _logTextBox.SelectionStart = _logTextBox.Text.Length;
        _logTextBox.ScrollToCaret();
    }

    private void PersistStationIdentity()
    {
        _settingsStore.UpdateStationIdentity(_stationIdTextBox.Text, _lineNameTextBox.Text);
    }

    private void ConfigureButton(Button button, string text, EventHandler onClick)
    {
        button.Text = text;
        button.AutoSize = true;
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
            BeginInvoke(action);
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
            AppendLog($"Preview load failed: {ex.Message}");
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
