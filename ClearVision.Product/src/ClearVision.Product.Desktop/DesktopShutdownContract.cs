namespace ClearVision.Product.Desktop;

internal enum DesktopShutdownCloseState
{
    NotStarted,
    Preparing,
    Prepared
}

internal static class DesktopShutdownContract
{
    internal const string IsolationRootEnvironmentVariable = "CV_DESKTOP_ISOLATION_ROOT";
    internal const string RepositoryRootEnvironmentVariable = "CV_DESKTOP_REPOSITORY_ROOT";
    internal const string UnattendedShutdownEnvironmentVariable = "CV_DESKTOP_UNATTENDED_SHUTDOWN";

    internal static readonly TimeSpan FlushDeadline = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan WebViewDisposeDeadline = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan HostStopDeadline = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan ProcessMargin = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan RunnerTotalDeadline =
        FlushDeadline + WebViewDisposeDeadline + HostStopDeadline + ProcessMargin;

    internal static readonly IReadOnlyList<string> ManagedPathEnvironmentVariables =
    [
        "Database__Path",
        "CV_WEBVIEW2_USER_DATA_FOLDER",
        "CV_CONVERSATION_STORE_ROOT",
        "CV_AGENT_RUN_EVENT_STORE",
        "CV_AI_HANDOFF_STORE_ROOT",
        "CV_DESKTOP_LOG_PATH",
        "CV_DESKTOP_SHUTDOWN_DIAGNOSTICS_PATH"
    ];

    internal static bool IsUnattendedShutdownEnabled(
        Func<string, string?> getEnvironment)
    {
        ArgumentNullException.ThrowIfNull(getEnvironment);

        if (!IsUnattendedShutdownRequested(getEnvironment))
        {
            return false;
        }

        var isolationRoot = getEnvironment(IsolationRootEnvironmentVariable);
        var repositoryRoot = getEnvironment(RepositoryRootEnvironmentVariable);
        return IsValidIsolationRoot(isolationRoot, repositoryRoot) &&
            ManagedPathEnvironmentVariables.All(name =>
                IsPathWithinIsolationRoot(getEnvironment(name), isolationRoot, repositoryRoot));
    }

    internal static bool IsUnattendedShutdownRequested(
        Func<string, string?> getEnvironment)
    {
        ArgumentNullException.ThrowIfNull(getEnvironment);
        return string.Equals(
            getEnvironment(UnattendedShutdownEnvironmentVariable),
            "1",
            StringComparison.Ordinal);
    }

    internal static bool IsValidIsolationRoot(
        string? isolationRoot,
        string? repositoryRoot = null)
    {
        if (!TryGetFullyQualifiedPath(isolationRoot, rejectTraversal: true, out var fullRoot))
        {
            return false;
        }

        var segments = GetPathSegments(fullRoot!);
        var temporaryIndex = Array.FindIndex(
            segments,
            segment => string.Equals(segment, ".tmp", StringComparison.OrdinalIgnoreCase));
        if (temporaryIndex < 0 || temporaryIndex >= segments.Length - 1)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return true;
        }

        if (!TryGetFullyQualifiedPath(repositoryRoot, rejectTraversal: true, out var fullRepositoryRoot))
        {
            return false;
        }

        var repositoryTemporaryRoot = Path.Combine(fullRepositoryRoot!, ".tmp");
        var repositoryTemporaryPrefix = repositoryTemporaryRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return fullRoot!.StartsWith(repositoryTemporaryPrefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsPathWithinIsolationRoot(
        string? path,
        string? isolationRoot,
        string? repositoryRoot = null)
    {
        if (!IsValidIsolationRoot(isolationRoot, repositoryRoot) ||
            !TryGetFullyQualifiedPath(path, rejectTraversal: true, out var fullPath) ||
            !TryGetFullyQualifiedPath(isolationRoot, rejectTraversal: true, out var fullRoot))
        {
            return false;
        }

        var rootPrefix = fullRoot!.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return fullPath!.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static TimeSpan? DeadlineForStage(string stage)
    {
        return stage.Trim().ToLowerInvariant() switch
        {
            "flush-start" or "workspace-flush" or "ai-flush" => FlushDeadline,
            "webview-dispose" => WebViewDisposeDeadline,
            "host-stop" => HostStopDeadline,
            "process-exit" => ProcessMargin,
            _ => null
        };
    }

    internal static bool TryBeginPreparation(ref DesktopShutdownCloseState state)
    {
        if (state != DesktopShutdownCloseState.NotStarted)
        {
            return false;
        }

        state = DesktopShutdownCloseState.Preparing;
        return true;
    }

    internal static void RestoreAfterDecline(ref DesktopShutdownCloseState state)
    {
        state = DesktopShutdownCloseState.NotStarted;
    }

    internal static bool TryMarkPrepared(ref DesktopShutdownCloseState state)
    {
        if (state != DesktopShutdownCloseState.Preparing)
        {
            return false;
        }

        state = DesktopShutdownCloseState.Prepared;
        return true;
    }

    private static bool TryGetFullyQualifiedPath(
        string? path,
        bool rejectTraversal,
        out string? fullPath)
    {
        fullPath = null;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
        {
            return false;
        }

        var trimmed = path.Trim();
        if (rejectTraversal && ContainsParentTraversal(trimmed))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(trimmed);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool ContainsParentTraversal(string path)
    {
        return path.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "..");
    }

    private static string[] GetPathSegments(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        return path[root.Length..].Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
    }
}
