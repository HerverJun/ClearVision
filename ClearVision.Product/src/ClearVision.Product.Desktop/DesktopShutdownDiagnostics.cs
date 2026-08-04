using System.Diagnostics;

namespace ClearVision.Product.Desktop;

internal enum DesktopShutdownStageStatus
{
    Started,
    Succeeded,
    Failed,
    Timeout,
    Unknown,
    Skipped,
    ForcedExit
}

internal sealed class DesktopShutdownDiagnostics
{
    internal const string DiagnosticsPathEnvironmentVariable = "CV_DESKTOP_SHUTDOWN_DIAGNOSTICS_PATH";
    internal const string UnattendedShutdownEnvironmentVariable = "CV_DESKTOP_UNATTENDED_SHUTDOWN";

    private readonly object _sync = new();
    private readonly string? _path;
    private bool _forcedExit;

    private DesktopShutdownDiagnostics(string? path)
    {
        _path = NormalizePath(path);
    }

    internal static DesktopShutdownDiagnostics FromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable(DiagnosticsPathEnvironmentVariable);
        try
        {
            return new DesktopShutdownDiagnostics(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DesktopShutdown] Could not initialize diagnostics path: {ex}");
            return new DesktopShutdownDiagnostics(null);
        }
    }

    internal bool ForcedExit
    {
        get
        {
            lock (_sync)
            {
                return _forcedExit;
            }
        }
    }

    internal StageScope BeginStage(string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        var startedAt = Stopwatch.GetTimestamp();
        RecordStage(stage, DesktopShutdownStageStatus.Started, TimeSpan.Zero, null, ForcedExit);
        return new(this, stage, startedAt);
    }

    internal void MarkForcedExit(string reason)
    {
        lock (_sync)
        {
            _forcedExit = true;
        }

        RecordStage(
            "forced-exit",
            DesktopShutdownStageStatus.ForcedExit,
            TimeSpan.Zero,
            string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim(),
            forcedExit: true);
    }

    internal void RecordStage(
        string stage,
        DesktopShutdownStageStatus status,
        TimeSpan elapsed,
        string? error,
        bool forcedExit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);

        var normalizedError = string.IsNullOrWhiteSpace(error)
            ? null
            : error.Trim().Length > 2000
                ? error.Trim()[..2000]
                : error.Trim();
        var statusText = status.ToString().ToLowerInvariant();
        var elapsedMilliseconds = Math.Max(0, (long)Math.Round(elapsed.TotalMilliseconds));
        var message = $"[DesktopShutdown] stage={stage} status={statusText} " +
            $"elapsedMs={elapsedMilliseconds} forcedExit={forcedExit}" +
            (normalizedError is null ? string.Empty : $" error={normalizedError}");

        Debug.WriteLine(message);
        try
        {
            Serilog.Log.Logger.Information(
                "Desktop shutdown stage {Stage} completed with {Status} in {ElapsedMilliseconds} ms. Forced exit: {ForcedExit}. Error: {Error}",
                stage,
                statusText,
                elapsedMilliseconds,
                forcedExit,
                normalizedError ?? string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DesktopShutdown] Structured log write failed: {ex}");
        }

        if (_path is null)
        {
            return;
        }

        var record = new
        {
            schemaVersion = 1,
            capturedAtUtc = DateTime.UtcNow,
            stage,
            status = statusText,
            elapsedMilliseconds,
            error = normalizedError,
            forcedExit
        };

        try
        {
            var json = JsonSerializer.Serialize(record);
            lock (_sync)
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(_path, json + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DesktopShutdown] Diagnostics file write failed: {ex}");
        }
    }

    private static string? NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());
    }

    internal sealed class StageScope : IDisposable
    {
        private readonly DesktopShutdownDiagnostics _owner;
        private readonly string _stage;
        private readonly long _startedAt;
        private bool _completed;

        internal StageScope(DesktopShutdownDiagnostics owner, string stage, long startedAt)
        {
            _owner = owner;
            _stage = stage;
            _startedAt = startedAt;
        }

        internal void Complete(
            DesktopShutdownStageStatus status,
            string? error = null,
            bool? forcedExit = null)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _owner.RecordStage(
                _stage,
                status,
                Stopwatch.GetElapsedTime(_startedAt),
                error,
                forcedExit ?? _owner.ForcedExit);
        }

        public void Dispose()
        {
            Complete(
                DesktopShutdownStageStatus.Unknown,
                "stage scope disposed before an explicit terminal status");
        }
    }
}
