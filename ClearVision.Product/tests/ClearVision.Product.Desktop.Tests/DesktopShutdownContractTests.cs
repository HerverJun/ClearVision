using ClearVision.Product.Desktop;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

public class DesktopShutdownContractTests
{
    [Fact]
    public void CloseStateMachine_ShouldCancelPreparationAndAllowOnlyPreparedClose()
    {
        var state = DesktopShutdownCloseState.NotStarted;

        DesktopShutdownContract.TryBeginPreparation(ref state).Should().BeTrue();
        state.Should().Be(DesktopShutdownCloseState.Preparing);
        DesktopShutdownContract.TryBeginPreparation(ref state).Should().BeFalse();
        DesktopShutdownContract.TryMarkPrepared(ref state).Should().BeTrue();
        state.Should().Be(DesktopShutdownCloseState.Prepared);
        DesktopShutdownContract.TryMarkPrepared(ref state).Should().BeFalse();
    }

    [Fact]
    public void CloseStateMachine_ShouldRestoreNotStartedAfterUserDeclines()
    {
        var state = DesktopShutdownCloseState.NotStarted;

        DesktopShutdownContract.TryBeginPreparation(ref state).Should().BeTrue();
        DesktopShutdownContract.RestoreAfterDecline(ref state);

        state.Should().Be(DesktopShutdownCloseState.NotStarted);
        DesktopShutdownContract.TryBeginPreparation(ref state).Should().BeTrue();
    }

    [Fact]
    public void ShutdownDeadlines_ShouldUseOneContractAcrossStages()
    {
        DesktopShutdownContract.DeadlineForStage("flush-start").Should().Be(DesktopShutdownContract.FlushDeadline);
        DesktopShutdownContract.DeadlineForStage("workspace-flush").Should().Be(DesktopShutdownContract.FlushDeadline);
        DesktopShutdownContract.DeadlineForStage("ai-flush").Should().Be(DesktopShutdownContract.FlushDeadline);
        DesktopShutdownContract.DeadlineForStage("webview-dispose").Should().Be(DesktopShutdownContract.WebViewDisposeDeadline);
        DesktopShutdownContract.DeadlineForStage("host-stop").Should().Be(DesktopShutdownContract.HostStopDeadline);
        DesktopShutdownContract.DeadlineForStage("process-exit").Should().Be(DesktopShutdownContract.ProcessMargin);
        DesktopShutdownContract.RunnerTotalDeadline.Should().Be(TimeSpan.FromSeconds(25));
    }

    [Fact]
    public void UnattendedIsolation_ShouldAcceptValidChildPaths()
    {
        var environment = CreateValidEnvironment();

        DesktopShutdownContract.IsUnattendedShutdownEnabled(Get(environment)).Should().BeTrue();
    }

    [Fact]
    public void UnattendedIsolation_ShouldRejectSiblingPath()
    {
        var environment = CreateValidEnvironment();
        var isolationRoot = environment[DesktopShutdownContract.IsolationRootEnvironmentVariable]!;
        environment["CV_DESKTOP_LOG_PATH"] = Path.Combine(
            Directory.GetParent(isolationRoot)!.FullName,
            "sibling",
            "desktop.log");

        DesktopShutdownContract.IsUnattendedShutdownEnabled(Get(environment)).Should().BeFalse();
    }

    [Fact]
    public void UnattendedIsolation_ShouldRejectDifferentDotTmp()
    {
        var environment = CreateValidEnvironment();
        var otherRoot = Path.Combine(
            Path.GetPathRoot(environment[DesktopShutdownContract.IsolationRootEnvironmentVariable])!,
            "other",
            ".tmp",
            "run");
        environment[DesktopShutdownContract.IsolationRootEnvironmentVariable] = otherRoot;
        foreach (var name in DesktopShutdownContract.ManagedPathEnvironmentVariables)
        {
            environment[name] = Path.Combine(otherRoot, name.Replace("__", "-"));
        }

        DesktopShutdownContract.IsUnattendedShutdownEnabled(Get(environment)).Should().BeFalse();
    }

    [Fact]
    public void UnattendedIsolation_ShouldRejectPathTraversal()
    {
        var environment = CreateValidEnvironment();
        environment["CV_DESKTOP_LOG_PATH"] = environment[DesktopShutdownContract.IsolationRootEnvironmentVariable] +
            "\\..\\sibling\\desktop.log";

        DesktopShutdownContract.IsUnattendedShutdownEnabled(Get(environment)).Should().BeFalse();
    }

    [Fact]
    public void UnattendedIsolation_ShouldRejectMissingRoot()
    {
        var environment = CreateValidEnvironment();
        environment.Remove(DesktopShutdownContract.IsolationRootEnvironmentVariable);

        DesktopShutdownContract.IsUnattendedShutdownEnabled(Get(environment)).Should().BeFalse();
    }

    [Fact]
    public void UnattendedIsolation_ShouldRejectMissingManagedPath()
    {
        var environment = CreateValidEnvironment();
        environment.Remove("CV_AGENT_RUN_EVENT_STORE");

        DesktopShutdownContract.IsUnattendedShutdownEnabled(Get(environment)).Should().BeFalse();
    }

    [Fact]
    public void UnattendedShutdown_ShouldBeDisabledByProductionDefault()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);

        DesktopShutdownContract.IsUnattendedShutdownEnabled(Get(environment)).Should().BeFalse();
    }

    [Fact]
    public void MainFormShutdownImplementation_ShouldKeepDisposeBeforeSecondClose()
    {
        var source = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/MainForm.cs"));

        source.Should().NotContain("async void MainForm_FormClosing");
        source.Should().Contain("e.Cancel = true;");
        source.IndexOf("await DisposeWebViewHostAsync()", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("RequestFinalClose();", StringComparison.Ordinal));
        source.Should().Contain("DesktopShutdownCloseState.Prepared");
        source.Should().Contain("WebViewDisposeDeadline");
        source.Should().Contain("MarkForcedExit");
    }

    private static Dictionary<string, string?> CreateValidEnvironment()
    {
        var root = Path.Combine(
            Path.GetPathRoot(AppContext.BaseDirectory)!,
            "ClearVision",
            ".tmp",
            "f09-r2",
            Guid.NewGuid().ToString("N"));
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [DesktopShutdownContract.IsolationRootEnvironmentVariable] = root,
            [DesktopShutdownContract.RepositoryRootEnvironmentVariable] = Path.Combine(
                Path.GetPathRoot(root)!,
                "ClearVision"),
            [DesktopShutdownContract.UnattendedShutdownEnvironmentVariable] = "1"
        };

        foreach (var name in DesktopShutdownContract.ManagedPathEnvironmentVariables)
        {
            environment[name] = Path.Combine(root, name.Replace("__", "-"));
        }

        return environment;
    }

    private static Func<string, string?> Get(IReadOnlyDictionary<string, string?> environment) =>
        name => environment.TryGetValue(name, out var value) ? value : null;

    private static string RepoPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
