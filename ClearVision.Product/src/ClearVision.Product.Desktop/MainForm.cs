// MainForm.cs
// 初始化菜单栏
// 作者：蘅芜君

using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using ClearVision.Product.Desktop.Configuration;
using ClearVision.Product.Desktop.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.WinForms;

namespace ClearVision.Product.Desktop;

/// <summary>
/// 主窗体，集成 WebView2 控件。
/// </summary>
public partial class MainForm : Form
{
    private const string ForceCloseWarning =
        "Formal Run may still be executing or its outcome may be unknown. Force-closing only releases local resources; it is not a successful Stop and may leave the server run active or lose the Results handoff. Force close anyway?";
    private readonly WebView2 _webView;
    private readonly WebView2Host _webView2Host;
    private readonly Handlers.WebMessageHandler? _messageHandler;
    private readonly EnterPhotoelectricTriggerInputService? _triggerInputService;
    private readonly DesktopShutdownDiagnostics _shutdownDiagnostics;
    private bool _allowClose;
    private bool _closingInProgress;
    private bool _disposedWebViewHost;

    public MainForm()
    {
        InitializeComponent();

        // 创建 WebView2 控件
        _webView = new WebView2
        {
            Dock = DockStyle.Fill
        };
        Controls.Add(_webView);

        // 创建 WebView2 宿主
        _messageHandler = Program.ServiceProvider?.GetService<Handlers.WebMessageHandler>();
        _triggerInputService = Program.ServiceProvider?.GetService<EnterPhotoelectricTriggerInputService>();
        _shutdownDiagnostics = Program.ShutdownDiagnostics;
        var studioOptions = Program.ServiceProvider?.GetService<IOptions<StudioOptions>>()?.Value
            ?? new StudioOptions();
        _webView2Host = new WebView2Host(_webView, _messageHandler, studioOptions);

        // 窗体加载时初始化 WebView2
        Load += MainForm_Load;
        FormClosing += MainForm_FormClosing;
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            // 初始化菜单栏（在WebView2初始化之前）
            InitializeMenu();

            var userDataFolder = Environment.GetEnvironmentVariable(
                "CV_WEBVIEW2_USER_DATA_FOLDER");
            await _webView2Host.InitializeAsync(userDataFolder);

            // S4-006: 初始化 WebMessage 处理器，挂载到 WebView2
            if (_webView.CoreWebView2 != null)
            {
                _messageHandler?.Initialize(_webView);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WebView2 初始化失败: {ex.Message}",
                "错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _triggerInputService?.AttachWindow(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        _triggerInputService?.HandleWindowMessage(m.Msg, m.LParam);
        base.WndProc(ref m);
    }

    /// <summary>
    /// 初始化菜单栏
    /// </summary>
    private void InitializeMenu()
    {
        var menuStrip = new MenuStrip();

        // 视图菜单
        var viewMenu = new ToolStripMenuItem("视图");

        // 【科学方案三】强制刷新菜单项
        var refreshMenuItem = new ToolStripMenuItem("强制刷新 (清除缓存)", null, async (s, e) =>
        {
            try
            {
                await _webView2Host.ForceReloadAsync();
                MessageBox.Show("缓存已清除，页面已刷新", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刷新失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        });
        refreshMenuItem.ShortcutKeys = Keys.F5 | Keys.Control;
        refreshMenuItem.ToolTipText = "Ctrl+F5 强制刷新";

        viewMenu.DropDownItems.Add(refreshMenuItem);

        // 开发工具菜单项（仅DEBUG模式）
#if DEBUG
        viewMenu.DropDownItems.Add(new ToolStripSeparator());

        var devToolsMenuItem = new ToolStripMenuItem("开发者工具", null, (s, e) =>
        {
            _webView.CoreWebView2?.OpenDevToolsWindow();
        });
        devToolsMenuItem.ShortcutKeys = Keys.F12;
        viewMenu.DropDownItems.Add(devToolsMenuItem);

        // 清除缓存菜单项
        var clearCacheMenuItem = new ToolStripMenuItem("清除缓存", null, async (s, e) =>
        {
            try
            {
                await _webView2Host.ClearCacheAsync();
                MessageBox.Show("缓存已清除", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"清除缓存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        });
        viewMenu.DropDownItems.Add(clearCacheMenuItem);
#endif

        menuStrip.Items.Add(viewMenu);

        // 将菜单栏添加到窗体
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            await DisposeWebViewHostAsync();
            return;
        }

        if (_closingInProgress)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _closingInProgress = true;

        var flushStage = _shutdownDiagnostics.BeginStage("flush-start");
        try
        {
            var timeout = e.CloseReason == CloseReason.WindowsShutDown
                ? TimeSpan.FromSeconds(2)
                : TimeSpan.FromSeconds(5);
            var flush = await FlushWorkspaceBeforeCloseAsync(timeout);
            flushStage.Complete(flush.Status, flush.Error);
            if (!flush.Succeeded)
            {
                var unattended = IsUnattendedShutdownEnabled();
                if (e.CloseReason == CloseReason.WindowsShutDown || unattended)
                {
                    _shutdownDiagnostics.MarkForcedExit(
                        unattended
                            ? $"unattended-flush-{flush.Status.ToString().ToLowerInvariant()}"
                            : $"windows-shutdown-flush-{flush.Status.ToString().ToLowerInvariant()}");
                }
                else if (PromptForceClose() != DialogResult.Yes)
                {
                    _closingInProgress = false;
                    return;
                }
                else
                {
                    _shutdownDiagnostics.MarkForcedExit("user-confirmed-force-close");
                }
            }

            _allowClose = true;
            BeginInvoke(new Action(Close));
        }
        catch (Exception ex)
        {
            flushStage.Complete(DesktopShutdownStageStatus.Unknown, ex.ToString());
            System.Diagnostics.Debug.WriteLine($"[MainForm] Shutdown coordination failed: {ex}");
            if (IsUnattendedShutdownEnabled())
            {
                _shutdownDiagnostics.MarkForcedExit("unattended-shutdown-coordination-exception");
                _allowClose = true;
                BeginInvoke(new Action(Close));
                return;
            }

            if (PromptForceClose() == DialogResult.Yes)
            {
                _shutdownDiagnostics.MarkForcedExit("user-confirmed-force-close-after-exception");
                _allowClose = true;
                BeginInvoke(new Action(Close));
                return;
            }

            _closingInProgress = false;
        }
        finally
        {
            flushStage.Dispose();
        }
    }

    private async Task<FlushWorkspaceResult> FlushWorkspaceBeforeCloseAsync(TimeSpan timeout)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var operationId = Guid.NewGuid().ToString("N");
            var startResult = await ExecuteScriptWithTimeoutAsync(
                BuildAiWorkspaceFlushStartScript(operationId, "host_close"),
                timeout);
            if (startResult is null)
            {
                return RecordFlushFailure(
                    DesktopShutdownStageStatus.Timeout,
                    "WebView2 did not acknowledge the flush start script.");
            }
            if (!ParseBooleanScriptResult(startResult))
            {
                return RecordFlushFailure(
                    DesktopShutdownStageStatus.Failed,
                    "WebView2 rejected the flush start script.");
            }

            while (stopwatch.Elapsed < timeout)
            {
                var remaining = timeout - stopwatch.Elapsed;
                var rawStatus = await ExecuteScriptWithTimeoutAsync(
                    BuildAiWorkspaceFlushStatusScript(operationId),
                    remaining);
                var status = ParseAiWorkspaceFlushStatus(rawStatus);
                if (status.IsTerminal)
                {
                    return RecordFlushStatus(status);
                }

                var delay = TimeSpan.FromMilliseconds(Math.Min(
                    50,
                    Math.Max(1, (timeout - stopwatch.Elapsed).TotalMilliseconds)));
                await Task.Delay(delay);
            }

            return RecordFlushFailure(
                DesktopShutdownStageStatus.Timeout,
                "Workspace flush did not reach a terminal state before the timeout.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] workspace flush before close failed: {ex}");
            return RecordFlushFailure(DesktopShutdownStageStatus.Unknown, ex.ToString());
        }
    }

    private FlushWorkspaceResult RecordFlushStatus(AiWorkspaceFlushStatus status)
    {
        var workspaceStatus = StageStatusForFlushOutcome(status.WorkspaceOutcome, DesktopShutdownStageStatus.Unknown);
        var aiStatus = StageStatusForFlushOutcome(status.AiOutcome, DesktopShutdownStageStatus.Unknown);
        _shutdownDiagnostics.RecordStage(
            "workspace-flush",
            workspaceStatus,
            TimeSpan.Zero,
            status.Error,
            _shutdownDiagnostics.ForcedExit);
        _shutdownDiagnostics.RecordStage(
            "ai-flush",
            aiStatus,
            TimeSpan.Zero,
            status.Error,
            _shutdownDiagnostics.ForcedExit);
        return new(
            status.Succeeded,
            status.Succeeded ? DesktopShutdownStageStatus.Succeeded : StageStatusForFlushOutcome(status.Outcome, DesktopShutdownStageStatus.Unknown),
            status.Error);
    }

    private FlushWorkspaceResult RecordFlushFailure(DesktopShutdownStageStatus status, string error)
    {
        _shutdownDiagnostics.RecordStage(
            "workspace-flush",
            status,
            TimeSpan.Zero,
            error,
            _shutdownDiagnostics.ForcedExit);
        _shutdownDiagnostics.RecordStage(
            "ai-flush",
            status,
            TimeSpan.Zero,
            error,
            _shutdownDiagnostics.ForcedExit);
        return new(false, status, error);
    }

    private static DesktopShutdownStageStatus StageStatusForFlushOutcome(
        string outcome,
        DesktopShutdownStageStatus fallback)
    {
        return outcome.Trim().ToLowerInvariant() switch
        {
            "succeeded" or "completed" => DesktopShutdownStageStatus.Succeeded,
            "skipped" => DesktopShutdownStageStatus.Skipped,
            "failed" => DesktopShutdownStageStatus.Failed,
            "timeout" => DesktopShutdownStageStatus.Timeout,
            "unknown" => DesktopShutdownStageStatus.Unknown,
            _ => fallback
        };
    }

    private DialogResult PromptForceClose()
    {
        using var stage = _shutdownDiagnostics.BeginStage("messagebox-confirmation");
        try
        {
            var choice = MessageBox.Show(
                ForceCloseWarning,
                "ClearVision",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            stage.Complete(
                choice == DialogResult.Yes
                    ? DesktopShutdownStageStatus.Succeeded
                    : DesktopShutdownStageStatus.Skipped,
                choice == DialogResult.Yes ? null : "user-declined-force-close");
            return choice;
        }
        catch (Exception ex)
        {
            stage.Complete(DesktopShutdownStageStatus.Failed, ex.ToString());
            throw;
        }
    }

    private static bool IsUnattendedShutdownEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(DesktopShutdownDiagnostics.UnattendedShutdownEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return false;
        }

        var paths = new[]
        {
            Environment.GetEnvironmentVariable(DesktopShutdownDiagnostics.DiagnosticsPathEnvironmentVariable),
            Environment.GetEnvironmentVariable("Database__Path"),
            Environment.GetEnvironmentVariable("CV_WEBVIEW2_USER_DATA_FOLDER"),
            Environment.GetEnvironmentVariable("CV_CONVERSATION_STORE_ROOT"),
            Environment.GetEnvironmentVariable("CV_AGENT_RUN_EVENT_STORE"),
            Environment.GetEnvironmentVariable("CV_AI_HANDOFF_STORE_ROOT"),
            Environment.GetEnvironmentVariable("CV_DESKTOP_LOG_PATH")
        };
        return paths.All(IsUnderDotTmp);
    }

    private static bool IsUnderDotTmp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var fullPath = Path.GetFullPath(path.Trim());
            var root = Path.GetPathRoot(fullPath) ?? string.Empty;
            var relative = fullPath[root.Length..];
            return relative.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(part => string.Equals(part, ".tmp", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> ExecuteScriptWithTimeoutAsync(string script, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return null;
        }

        var scriptTask = _webView2Host.ExecuteScriptAsync(script);
        var completed = await Task.WhenAny(scriptTask, Task.Delay(timeout));
        return completed == scriptTask
            ? await scriptTask
            : null;
    }

    internal static string BuildAiWorkspaceFlushStartScript(string operationId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var operationIdJson = JsonSerializer.Serialize(operationId);
        var reasonJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(reason) ? "host_close" : reason);
        return $$"""
            (() => {
                const operationId = {{operationIdJson}};
                const reason = {{reasonJson}};
                const storeName = '__clearVisionAiWorkspaceFlushResults';
                const store = window[storeName] || (window[storeName] = {});
                store[operationId] = { status: 'pending', value: false, error: '', startedAt: Date.now() };
                try {
                    const skipped = { status: 'skipped', value: true, error: '' };
                    const flushers = [
                        { name: 'workspace', flush: window.__clearVisionFlushProjectWorkspace },
                        { name: 'ai', flush: window.__clearVisionFlushAiPanelWorkspace }
                    ].filter(item => typeof item.flush === 'function');
                    if (flushers.length === 0) {
                        store[operationId] = {
                            status: 'completed',
                            value: true,
                            error: '',
                            workspace: skipped,
                            ai: skipped,
                            startedAt: Date.now()
                        };
                        return true;
                    }

                    const invoke = item => Promise.resolve()
                        .then(() => item.flush(reason))
                        .then(value => ({
                            name: item.name,
                            status: 'completed',
                            value: value === true,
                            error: ''
                        }))
                        .catch(error => ({
                            name: item.name,
                            status: 'failed',
                            value: false,
                            error: String(error && (error.message || error) || 'unknown')
                        }));
                    Promise.all(flushers.map(invoke))
                        .then(results => {
                            const workspace = results.find(result => result.name === 'workspace') || skipped;
                            const ai = results.find(result => result.name === 'ai') || skipped;
                            const succeeded = [workspace, ai].every(result =>
                                result.status === 'completed' && result.value === true ||
                                result.status === 'skipped');
                            store[operationId] = {
                                status: 'completed',
                                value: succeeded,
                                error: results.filter(result => result.error).map(result => result.error).join('; '),
                                workspace,
                                ai,
                                startedAt: Date.now()
                            };
                        });
                    return true;
                } catch (error) {
                    store[operationId] = {
                        status: 'failed',
                        value: false,
                        error: String(error && (error.message || error) || 'unknown'),
                        startedAt: Date.now()
                    };
                    return false;
                }
            })()
            """;
    }

    internal static string BuildAiWorkspaceFlushStatusScript(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var operationIdJson = JsonSerializer.Serialize(operationId);
        return $$"""
            (() => {
                const operationId = {{operationIdJson}};
                const store = window.__clearVisionAiWorkspaceFlushResults || {};
                const state = store[operationId] || { status: 'missing', value: false, error: '' };
                if (state.status === 'completed' || state.status === 'failed' || state.status === 'missing') {
                    delete store[operationId];
                }
                return state;
            })()
            """;
    }

    internal static bool ParseBooleanScriptResult(string? rawResult)
    {
        if (string.IsNullOrWhiteSpace(rawResult))
        {
            return false;
        }

        try
        {
            return JsonSerializer.Deserialize<bool>(rawResult);
        }
        catch (JsonException)
        {
            return string.Equals(rawResult.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static AiWorkspaceFlushStatus ParseAiWorkspaceFlushStatus(string? rawResult)
    {
        if (rawResult is null)
        {
            return AiWorkspaceFlushStatus.Unknown;
        }

        if (string.IsNullOrWhiteSpace(rawResult))
        {
            return AiWorkspaceFlushStatus.Failed;
        }

        try
        {
            using var document = JsonDocument.Parse(rawResult);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.True)
            {
                return AiWorkspaceFlushStatus.Success;
            }

            if (root.ValueKind == JsonValueKind.False)
            {
                return AiWorkspaceFlushStatus.Failed;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return AiWorkspaceFlushStatus.Failed;
            }

            var status = root.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : string.Empty;
            status = status?.Trim().ToLowerInvariant();
            if (status == "pending")
            {
                return AiWorkspaceFlushStatus.Pending;
            }

            if (status == "completed")
            {
                var succeeded = root.TryGetProperty("value", out var valueElement) &&
                    valueElement.ValueKind == JsonValueKind.True;
                return FromStructuredFlushStatus(root, succeeded, "completed");
            }

            if (status == "failed")
            {
                return FromStructuredFlushStatus(root, succeeded: false, "failed");
            }

            return AiWorkspaceFlushStatus.Failed;
        }
        catch (JsonException)
        {
            return AiWorkspaceFlushStatus.Failed;
        }
    }

    private static AiWorkspaceFlushStatus FromStructuredFlushStatus(
        JsonElement root,
        bool succeeded,
        string outcome)
    {
        var workspaceOutcome = ReadFlushStageOutcome(root, "workspace", outcome);
        var aiOutcome = ReadFlushStageOutcome(root, "ai", outcome);
        var error = root.TryGetProperty("error", out var errorElement) &&
            errorElement.ValueKind == JsonValueKind.String
            ? errorElement.GetString()
            : null;
        return new(true, succeeded, outcome == "completed" && succeeded ? "succeeded" : "failed",
            workspaceOutcome, aiOutcome, error);
    }

    private static string ReadFlushStageOutcome(JsonElement root, string property, string fallback)
    {
        if (!root.TryGetProperty(property, out var stage) || stage.ValueKind != JsonValueKind.Object)
        {
            return fallback == "completed" ? "unknown" : fallback;
        }

        if (stage.TryGetProperty("status", out var statusElement) &&
            statusElement.ValueKind == JsonValueKind.String)
        {
            var status = statusElement.GetString()?.Trim().ToLowerInvariant();
            if (status is "pending" or "succeeded" or "completed" or "failed" or "timeout" or "unknown" or "skipped")
            {
                if (status is "completed" &&
                    stage.TryGetProperty("value", out var valueElement) &&
                    valueElement.ValueKind == JsonValueKind.False)
                {
                    return "failed";
                }
                return status == "completed" ? "succeeded" : status!;
            }
        }

        return fallback == "completed" ? "unknown" : fallback;
    }

    internal readonly record struct AiWorkspaceFlushStatus(
        bool IsTerminal,
        bool Succeeded,
        string Outcome,
        string WorkspaceOutcome,
        string AiOutcome,
        string? Error)
    {
        public static AiWorkspaceFlushStatus Pending { get; } =
            new(false, false, "pending", "pending", "pending", null);
        public static AiWorkspaceFlushStatus Success { get; } =
            new(true, true, "succeeded", "succeeded", "skipped", null);
        public static AiWorkspaceFlushStatus Failed { get; } =
            new(true, false, "failed", "failed", "failed", null);
        public static AiWorkspaceFlushStatus Unknown { get; } =
            new(true, false, "unknown", "unknown", "unknown", "WebView2 script did not return a result.");
    }

    private readonly record struct FlushWorkspaceResult(
        bool Succeeded,
        DesktopShutdownStageStatus Status,
        string? Error);

    private async Task DisposeWebViewHostAsync()
    {
        using var stage = _shutdownDiagnostics.BeginStage("webview-dispose");
        if (_disposedWebViewHost)
        {
            stage.Complete(DesktopShutdownStageStatus.Skipped, "WebView2 host was already disposed.");
            return;
        }

        _disposedWebViewHost = true;
        try
        {
            _messageHandler?.Dispose();
            await _webView2Host.DisposeAsync();
            stage.Complete(DesktopShutdownStageStatus.Succeeded);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Shutdown cleanup failed: {ex}");
            stage.Complete(DesktopShutdownStageStatus.Failed, ex.ToString());
        }
    }

    private void InitializeComponent()
    {
        SuspendLayout();
        //
        // MainForm
        //
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1280, 720);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "ClearVision Product";
        ResumeLayout(false);
    }
}
