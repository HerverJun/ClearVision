param(
    [string]$Username = "admin",
    [string]$Password = $env:CV_SMOKE_PASSWORD,
    [string]$Configuration = "Debug",
    [string]$EvidenceDirectory = "quality/evidence/ai-webview2-release",
    [string]$DesktopExecutablePath,
    [string]$NodeSmokePath,
    [string]$NodeExecutablePath,
    [string]$DesktopPathEnvironment,
    [int]$WebPort = 5000,
    [int]$CdpPort = 9323,
    [double]$Scale = 1.0,
    [string]$Phase = "full",
    [string]$RunName = "smoke",
    [string]$HostLogDirectory,
    [string]$WebView2UserDataDirectory,
    [string]$ConversationStoreRoot,
    [string]$AgentRunStoreRoot,
    [string]$HandoffArtifactStoreRoot,
    [string]$IsolationRoot,
    [string]$RuntimeCleanupRoot,
    [string]$DatabasePath,
    [int]$WindowWidth = 1920,
    [int]$WindowHeight = 1080,
    [switch]$SingleRun,
    [switch]$KeepDatabase,
    [switch]$ReuseDatabase,
    [switch]$AllowInitialAdminSetup,
    [switch]$DeferAuthToScenario,
    [switch]$SanitizeDesktopPath,
    [switch]$UnattendedShutdown,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$shutdownFlushDeadlineSeconds = 5
$shutdownWebViewDisposeDeadlineSeconds = 5
$shutdownHostStopDeadlineSeconds = 10
$shutdownProcessMarginSeconds = 5
$shutdownRunnerTotalDeadlineSeconds =
    $shutdownFlushDeadlineSeconds +
    $shutdownWebViewDisposeDeadlineSeconds +
    $shutdownHostStopDeadlineSeconds +
    $shutdownProcessMarginSeconds
$shutdownExpectedDeadlinesMilliseconds = [ordered]@{
    "flush-start" = $shutdownFlushDeadlineSeconds * 1000
    "workspace-flush" = $shutdownFlushDeadlineSeconds * 1000
    "ai-flush" = $shutdownFlushDeadlineSeconds * 1000
    "webview-dispose" = $shutdownWebViewDisposeDeadlineSeconds * 1000
    "host-stop" = $shutdownHostStopDeadlineSeconds * 1000
    "process-exit" = $shutdownProcessMarginSeconds * 1000
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $scriptRoot))
$project = Join-Path $repoRoot "ClearVision.Product/src/ClearVision.Product.Desktop/ClearVision.Product.Desktop.csproj"
$uiTests = Join-Path $repoRoot "ClearVision.Product/tests/ClearVision.Product.UI.Tests"
$nodeSmoke = if ([string]::IsNullOrWhiteSpace($NodeSmokePath)) {
    Join-Path $uiTests "tests/e2e/ai-webview2-release-smoke.cjs"
} else {
    [System.IO.Path]::GetFullPath($NodeSmokePath)
}
$nodeExe = if ([string]::IsNullOrWhiteSpace($NodeExecutablePath)) {
    (Get-Command node.exe -ErrorAction Stop).Source
} else {
    [System.IO.Path]::GetFullPath($NodeExecutablePath)
}
$evidence = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $EvidenceDirectory))
$runtime = "net8.0-windows/win-x64"
$exe = if ([string]::IsNullOrWhiteSpace($DesktopExecutablePath)) {
    Join-Path $repoRoot "ClearVision.Product/src/ClearVision.Product.Desktop/bin/$Configuration/$runtime/ClearVision.Product.Desktop.exe"
} else {
    [System.IO.Path]::GetFullPath($DesktopExecutablePath)
}
$useIsolatedAuth = [string]::IsNullOrWhiteSpace($Password)
$repositoryTemporaryRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ".tmp"))
$repositoryTemporaryPrefix = $repositoryTemporaryRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar

$requestedIsolationRoot = if (-not [string]::IsNullOrWhiteSpace($IsolationRoot)) {
    $IsolationRoot
} elseif (-not [string]::IsNullOrWhiteSpace($RuntimeCleanupRoot)) {
    $RuntimeCleanupRoot
} else {
    Join-Path $repositoryTemporaryRoot (
        "desktop-unattended-" + [Guid]::NewGuid().ToString("N"))
}
if (-not [System.IO.Path]::IsPathRooted($requestedIsolationRoot) -or
    $requestedIsolationRoot -match '(^|[\\/])\.\.([\\/]|$)') {
    throw "IsolationRoot must be an absolute path without parent traversal."
}
$isolationRoot = [System.IO.Path]::GetFullPath($requestedIsolationRoot)
$isolationRootTrimmed = $isolationRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
if ([string]::Equals(
        $isolationRootTrimmed,
        $repositoryTemporaryRoot.TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar),
        [System.StringComparison]::OrdinalIgnoreCase) -or
    -not $isolationRoot.StartsWith(
        $repositoryTemporaryPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "IsolationRoot must be a child of the repository .tmp directory."
}
if (-not [string]::IsNullOrWhiteSpace($IsolationRoot) -and
    -not [string]::IsNullOrWhiteSpace($RuntimeCleanupRoot) -and
    -not [string]::Equals(
        $isolationRoot,
        [System.IO.Path]::GetFullPath($RuntimeCleanupRoot),
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "IsolationRoot and RuntimeCleanupRoot must identify the same path."
}

$isolationRootPrefix = $isolationRootTrimmed + [System.IO.Path]::DirectorySeparatorChar
$runtimeIsolationRoot = Join-Path $isolationRoot "runtime"
$runtimeCleanupBoundary = $isolationRoot
$hostLogs = if ([string]::IsNullOrWhiteSpace($HostLogDirectory)) {
    Join-Path $isolationRoot "host-logs"
} else {
    [System.IO.Path]::GetFullPath($HostLogDirectory)
}
$webView2UserDataRoot = if ([string]::IsNullOrWhiteSpace($WebView2UserDataDirectory)) {
    Join-Path $isolationRoot "webview2"
} else {
    [System.IO.Path]::GetFullPath($WebView2UserDataDirectory)
}
$conversationRoot = if ([string]::IsNullOrWhiteSpace($ConversationStoreRoot)) {
    Join-Path $isolationRoot "conversation"
} else {
    [System.IO.Path]::GetFullPath($ConversationStoreRoot)
}
$agentRunRoot = if ([string]::IsNullOrWhiteSpace($AgentRunStoreRoot)) {
    Join-Path $isolationRoot "agent-runs"
} else {
    [System.IO.Path]::GetFullPath($AgentRunStoreRoot)
}
$handoffArtifactRoot = if ([string]::IsNullOrWhiteSpace($HandoffArtifactStoreRoot)) {
    Join-Path $isolationRoot "handoffs"
} else {
    [System.IO.Path]::GetFullPath($HandoffArtifactStoreRoot)
}
$isolatedDatabase = if ([string]::IsNullOrWhiteSpace($DatabasePath)) {
    Join-Path $isolationRoot "database/vision.db"
} else {
    [System.IO.Path]::GetFullPath($DatabasePath)
}

function Assert-RepositoryTemporaryPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Label must be non-empty."
    }
    if (-not [System.IO.Path]::IsPathRooted($Path) -or
        $Path -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "$Label must be an absolute path without parent traversal."
    }

    $resolved = [System.IO.Path]::GetFullPath($Path)
    if ($resolved.StartsWith($isolationRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $resolved
    }

    throw "$Label must remain under the isolation root '$isolationRoot': $resolved"
}

$isolatedDatabase = Assert-RepositoryTemporaryPath `
    -Path $isolatedDatabase `
    -Label "DatabasePath"
$runtimeIsolationRoot = Assert-RepositoryTemporaryPath `
    -Path $runtimeIsolationRoot `
    -Label "RuntimeDirectory"
$webView2UserDataRoot = Assert-RepositoryTemporaryPath `
    -Path $webView2UserDataRoot `
    -Label "WebView2UserDataDirectory"
$conversationRoot = Assert-RepositoryTemporaryPath `
    -Path $conversationRoot `
    -Label "ConversationStoreRoot"
$agentRunRoot = Assert-RepositoryTemporaryPath `
    -Path $agentRunRoot `
    -Label "AgentRunStoreRoot"
$handoffArtifactRoot = Assert-RepositoryTemporaryPath `
    -Path $handoffArtifactRoot `
    -Label "HandoffArtifactStoreRoot"
$hostLogs = Assert-RepositoryTemporaryPath `
    -Path $hostLogs `
    -Label "HostLogDirectory"
$shutdownDiagnosticsPath = Assert-RepositoryTemporaryPath `
    -Path (Join-Path $hostLogs "$RunName-shutdown.jsonl") `
    -Label "Shutdown diagnostics path"

$environmentNames = @(
    "Database__Path",
    "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
    "CV_DESKTOP_HTTP_PORT",
    "CV_WEBVIEW2_USER_DATA_FOLDER",
    "CV_CONVERSATION_STORE_ROOT",
    "CV_AGENT_RUN_EVENT_STORE",
    "CV_AI_HANDOFF_STORE_ROOT",
    "CV_DESKTOP_ISOLATION_ROOT",
    "CV_DESKTOP_REPOSITORY_ROOT",
    "CV_DESKTOP_LOG_PATH",
    "CV_DESKTOP_SHUTDOWN_DIAGNOSTICS_PATH",
    "CV_DESKTOP_UNATTENDED_SHUTDOWN",
    "CV_CDP_PORT",
    "CV_WEB_PORT",
    "CV_DPI_SCALE",
    "CV_SMOKE_PHASE",
    "CV_SMOKE_TOKEN",
    "CV_SMOKE_USER",
    "CV_SMOKE_USERNAME",
    "CV_SMOKE_PASSWORD",
    "CV_EVIDENCE_DIR",
    "CV_DESKTOP_HOST_CLOSE_SIGNAL",
    "CV_NODE_COMPLETION_SIGNAL",
    "PATH"
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

function Set-ProcessEnvironment {
    param([string]$Name, [string]$Value)
    $normalizedName = if ([string]::Equals($Name, "PATH", [System.StringComparison]::OrdinalIgnoreCase)) {
        "Path"
    } else {
        $Name
    }
    [Environment]::SetEnvironmentVariable($normalizedName, $Value, "Process")
}

function Restore-ProcessEnvironment {
    foreach ($name in $environmentNames) {
        $value = $previousEnvironment[$name]
        Set-ProcessEnvironment -Name $name -Value $value
    }
}

function Remove-ItemWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath,
        [switch]$Recurse,
        [int]$MaxAttempts = 20,
        [int]$DelayMilliseconds = 250
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        if (-not (Test-Path -LiteralPath $LiteralPath)) {
            return
        }

        try {
            if ($Recurse) {
                Remove-Item -LiteralPath $LiteralPath -Recurse -Force -ErrorAction Stop
            } else {
                Remove-Item -LiteralPath $LiteralPath -Force -ErrorAction Stop
            }

            if (-not (Test-Path -LiteralPath $LiteralPath)) {
                return
            }

            throw "Removal completed without deleting '$LiteralPath'."
        } catch {
            if ($attempt -eq $MaxAttempts) {
                throw
            }
            Start-Sleep -Milliseconds $DelayMilliseconds
        }
    }
}

function Remove-RepositoryTemporaryDirectory {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Refusing to remove an empty isolation path."
    }

    $target = Assert-RepositoryTemporaryPath -Path $Path -Label "Cleanup path"
    if (Test-Path -LiteralPath $target) {
        Remove-ItemWithRetry -LiteralPath $target -Recurse
    }
}

function Resolve-DesktopProcessPath {
    if (-not [string]::IsNullOrWhiteSpace($DesktopPathEnvironment)) {
        return $DesktopPathEnvironment
    }
    if (-not $SanitizeDesktopPath) {
        return [Environment]::GetEnvironmentVariable("PATH", "Process")
    }

    $nodeDirectory = [System.IO.Path]::GetFullPath((Split-Path -Parent $nodeExe))
    $entries = @(
        ([Environment]::GetEnvironmentVariable("PATH", "Process") -split [System.IO.Path]::PathSeparator) |
            Where-Object {
                if ([string]::IsNullOrWhiteSpace($_)) {
                    return $false
                }
                try {
                    return [System.IO.Path]::GetFullPath($_.Trim()) -ne $nodeDirectory
                } catch {
                    return $true
                }
            }
    )
    return [string]::Join([System.IO.Path]::PathSeparator, $entries)
}

if ($WebPort -lt 1 -or $WebPort -gt 65535) {
    throw "WebPort must be between 1 and 65535."
}
if ($CdpPort -lt 1 -or $CdpPort -gt 65535) {
    throw "CdpPort must be between 1 and 65535."
}
if (-not (Test-Path -LiteralPath $nodeSmoke -PathType Leaf)) {
    throw "Node smoke scenario was not found: $nodeSmoke"
}
if (-not (Test-Path -LiteralPath $nodeExe -PathType Leaf)) {
    throw "Node executable was not found: $nodeExe"
}
if (($KeepDatabase -or $ReuseDatabase) -and [string]::IsNullOrWhiteSpace($DatabasePath)) {
    throw "KeepDatabase/ReuseDatabase require an explicit isolated DatabasePath."
}
if ($AllowInitialAdminSetup -and [string]::IsNullOrWhiteSpace($DatabasePath)) {
    throw "AllowInitialAdminSetup requires an explicit isolated DatabasePath."
}
if ($UnattendedShutdown -and [string]::IsNullOrWhiteSpace($DatabasePath)) {
    throw "UnattendedShutdown requires an explicit isolated DatabasePath."
}

Set-ProcessEnvironment -Name "CV_DESKTOP_HTTP_PORT" -Value ([string]$WebPort)
Set-ProcessEnvironment -Name "CV_DESKTOP_ISOLATION_ROOT" -Value $isolationRoot
Set-ProcessEnvironment -Name "CV_DESKTOP_REPOSITORY_ROOT" -Value $repoRoot
Set-ProcessEnvironment -Name "CV_CONVERSATION_STORE_ROOT" -Value $conversationRoot
Set-ProcessEnvironment -Name "CV_AGENT_RUN_EVENT_STORE" -Value $agentRunRoot
Set-ProcessEnvironment -Name "CV_AI_HANDOFF_STORE_ROOT" -Value $handoffArtifactRoot

$databaseDirectory = Split-Path -Parent $isolatedDatabase
if ($useIsolatedAuth -or -not [string]::IsNullOrWhiteSpace($DatabasePath)) {
    New-Item -ItemType Directory -Force -Path $databaseDirectory | Out-Null
    if (-not $ReuseDatabase) {
        foreach ($databaseArtifact in @(
            $isolatedDatabase,
            ($isolatedDatabase + "-shm"),
            ($isolatedDatabase + "-wal"))) {
            Remove-ItemWithRetry -LiteralPath $databaseArtifact
        }
    }
    Set-ProcessEnvironment -Name "Database__Path" -Value $isolatedDatabase
}

if ($useIsolatedAuth) {
    $randomBytes = New-Object byte[] 18
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($randomBytes) } finally { $random.Dispose() }
    $Password = ([Convert]::ToBase64String($randomBytes) + "Aa1!")
}

New-Item -ItemType Directory -Force -Path $evidence | Out-Null
New-Item -ItemType Directory -Force -Path $hostLogs | Out-Null
New-Item -ItemType Directory -Force -Path $webView2UserDataRoot | Out-Null
New-Item -ItemType Directory -Force -Path $conversationRoot | Out-Null
New-Item -ItemType Directory -Force -Path $agentRunRoot | Out-Null
New-Item -ItemType Directory -Force -Path $handoffArtifactRoot | Out-Null

if (-not $NoBuild) {
    & dotnet build $project -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Desktop build failed with exit code $LASTEXITCODE." }
}

if (-not (Test-Path -LiteralPath $exe)) {
    throw "Desktop executable was not found: $exe"
}

$existing = Get-Process ClearVision.Product.Desktop -ErrorAction SilentlyContinue
if ($existing) {
    throw "A ClearVision.Product.Desktop process is already running. Close it before the release smoke."
}

if (-not ("ClearVisionReleaseSmoke.NativeWindow" -as [type])) {
    Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ClearVisionReleaseSmoke {
    public static class NativeWindow {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        private static IntPtr Find(uint processId) {
            IntPtr target = IntPtr.Zero;
            EnumWindows((hWnd, _) => {
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid != processId) return true;
                var title = new StringBuilder(256);
                GetWindowText(hWnd, title, title.Capacity);
                if (title.ToString().IndexOf("ClearVision", StringComparison.OrdinalIgnoreCase) >= 0) {
                    target = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return target;
        }

        public static bool Resize(uint processId, int width, int height) {
            var target = Find(processId);
            const uint flags = 0x0002 | 0x0004 | 0x0010;
            return target != IntPtr.Zero && SetWindowPos(target, IntPtr.Zero, 0, 0, width, height, flags);
        }

        public static bool RequestClose(uint processId) {
            var target = Find(processId);
            return target != IntPtr.Zero && PostMessage(target, 0x0010, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
"@
}

function Wait-HttpEndpoint {
    param([string]$Uri, [int]$TimeoutSeconds = 45)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Uri -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) { return }
        } catch {
            Start-Sleep -Milliseconds 250
        }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for $Uri"
}

function Start-DesktopHost {
    param([int]$CdpPort, [double]$Scale, [string]$RunName)
    $stdout = Join-Path $hostLogs "$RunName-host.stdout.log"
    $stderr = Join-Path $hostLogs "$RunName-host.stderr.log"
    # Reuse one isolated profile across phases so process-restart probes can observe
    # the localStorage recovery markers written by the preceding phase.
    $runUserData = Assert-RepositoryTemporaryPath `
        -Path $webView2UserDataRoot `
        -Label "WebView2 run user data"
    New-Item -ItemType Directory -Force -Path $runUserData | Out-Null
    $shutdownDiagnosticsPath = Assert-RepositoryTemporaryPath `
        -Path (Join-Path $hostLogs "$RunName-shutdown.jsonl") `
        -Label "Shutdown diagnostics path"
    Set-ProcessEnvironment -Name "CV_WEBVIEW2_USER_DATA_FOLDER" -Value $runUserData
    Set-ProcessEnvironment -Name "CV_DESKTOP_LOG_PATH" -Value (Assert-RepositoryTemporaryPath `
        -Path (Join-Path $hostLogs "$RunName-desktop.log") `
        -Label "Desktop log path")
    Set-ProcessEnvironment -Name "CV_DESKTOP_SHUTDOWN_DIAGNOSTICS_PATH" -Value $shutdownDiagnosticsPath
    Set-ProcessEnvironment -Name "CV_DESKTOP_UNATTENDED_SHUTDOWN" -Value $(if ($UnattendedShutdown) { "1" } else { "0" })
    Set-ProcessEnvironment -Name "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS" -Value "--remote-debugging-port=$CdpPort --remote-allow-origins=* --force-device-scale-factor=$Scale"
    $runnerPath = [Environment]::GetEnvironmentVariable("PATH", "Process")
    Set-ProcessEnvironment -Name "PATH" -Value (Resolve-DesktopProcessPath)
    $process = Start-Process -FilePath $exe `
        -WorkingDirectory (Split-Path $exe) `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -WindowStyle Hidden `
        -PassThru
    Set-ProcessEnvironment -Name "PATH" -Value $runnerPath
    try {
        Wait-HttpEndpoint -Uri "http://127.0.0.1:$WebPort/api/auth/setup-status"
        Wait-HttpEndpoint -Uri "http://127.0.0.1:$CdpPort/json/version"
        if (-not [ClearVisionReleaseSmoke.NativeWindow]::Resize([uint32]$process.Id, $WindowWidth, $WindowHeight)) {
            throw "Could not resize the hidden WinForms window for PID $($process.Id)."
        }
        return $process
    } catch {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
        throw
    }
}

function Stop-DesktopHost {
    param([System.Diagnostics.Process]$Process)
    if ($Process.HasExited) { return }
    $requested = [ClearVisionReleaseSmoke.NativeWindow]::RequestClose([uint32]$Process.Id)
    if (-not $requested) {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        throw "Could not locate the hidden WinForms window for PID $($Process.Id)."
    }
    if (-not $Process.WaitForExit($shutdownRunnerTotalDeadlineSeconds * 1000)) {
        Stop-Process -Id $Process.Id -Force
        throw "Desktop Host did not complete its close/flush path within $shutdownRunnerTotalDeadlineSeconds seconds."
    }
}

function Read-ShutdownDiagnostics {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $records = [System.Collections.Generic.List[object]]::new()
    $parseErrors = [System.Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8) {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }
            try {
                $records.Add(($line | ConvertFrom-Json))
            } catch {
                $parseErrors.Add($_.Exception.Message)
            }
        }
    }

    $requiredStages = @(
        "flush-start",
        "workspace-flush",
        "ai-flush",
        "webview-dispose",
        "host-stop",
        "process-exit"
    )
    $stageStatuses = [ordered]@{}
    $deadlineViolations = [System.Collections.Generic.List[string]]::new()
    foreach ($stage in $requiredStages) {
        $terminal = @($records | Where-Object {
            [string]$_.stage -eq $stage -and [string]$_.status -ne "started"
        })
        $stageStatuses[$stage] = if ($terminal.Count -eq 0) {
            $null
        } else {
            [string]$terminal[$terminal.Count - 1].status
        }
        $expectedDeadline = $shutdownExpectedDeadlinesMilliseconds[$stage]
        if ($terminal.Count -eq 0 -or
            [int64]$terminal[$terminal.Count - 1].deadlineMilliseconds -ne $expectedDeadline) {
            $deadlineViolations.Add($stage)
        }
    }

    $forcedExit = @($records | Where-Object {
        ([bool]$_.forcedExit) -or
            [string]$_.stage -eq "forced-exit" -or
            [string]$_.status -eq "forcedexit"
    }).Count -gt 0
    $uncertain = @($records | Where-Object {
        [string]$_.status -in @("failed", "timeout", "unknown", "forcedexit")
    }).Count -gt 0
    $stageResultsPassed = $true
    foreach ($stage in $requiredStages) {
        if ([string]$stageStatuses[$stage] -notin @("succeeded", "skipped")) {
            $stageResultsPassed = $false
            break
        }
    }

    [pscustomobject]@{
        path = $Path
        recordCount = $records.Count
        forcedExit = $forcedExit
        uncertain = $uncertain
        parseErrors = @($parseErrors)
        deadlineViolations = @($deadlineViolations)
        expectedDeadlinesMilliseconds = $shutdownExpectedDeadlinesMilliseconds
        stages = $stageStatuses
        passed = $records.Count -gt 0 -and
            $parseErrors.Count -eq 0 -and
            $deadlineViolations.Count -eq 0 -and
            -not $forcedExit -and
            -not $uncertain -and
            $stageResultsPassed
    }
}

function Get-AuthSession {
    $body = @{ username = $Username; password = $Password } | ConvertTo-Json -Compress
    try {
        return Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$WebPort/api/auth/login" -ContentType "application/json" -Body $body
    } catch {
        if (-not $useIsolatedAuth -and -not $AllowInitialAdminSetup) { throw }
        $setupBody = @{
            username = $Username
            password = $Password
            confirmPassword = $Password
        } | ConvertTo-Json -Compress
        return Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$WebPort/api/auth/setup-admin" -ContentType "application/json" -Body $setupBody
    }
}

function Invoke-WebViewSmoke {
    param(
        [int]$CdpPort,
        [double]$Scale,
        [string]$Phase,
        [string]$RunName,
        [System.Diagnostics.Process]$DesktopProcess
    )
    $login = if ($DeferAuthToScenario) { $null } else { Get-AuthSession }
    $closeSignal = Join-Path $hostLogs "$RunName-host-close-ready.signal"
    $closeAcknowledgement = "$closeSignal.closed"
    $nodeCompletionSignal = "$closeSignal.node-complete"
    $nodeStdout = Join-Path $hostLogs "$RunName-node.stdout.log"
    $nodeStderr = Join-Path $hostLogs "$RunName-node.stderr.log"
    Remove-Item -LiteralPath $closeSignal, $closeAcknowledgement, $nodeCompletionSignal -Force -ErrorAction SilentlyContinue
    $env:CV_CDP_PORT = [string]$CdpPort
    $env:CV_WEB_PORT = [string]$WebPort
    $env:CV_DPI_SCALE = [string]$Scale
    $env:CV_SMOKE_PHASE = $Phase
    if ($login) {
        $env:CV_SMOKE_TOKEN = [string]$login.token
        $env:CV_SMOKE_USER = ($login.user | ConvertTo-Json -Compress)
    } else {
        Remove-Item Env:CV_SMOKE_TOKEN -ErrorAction SilentlyContinue
        Remove-Item Env:CV_SMOKE_USER -ErrorAction SilentlyContinue
    }
    $env:CV_SMOKE_USERNAME = $Username
    $env:CV_SMOKE_PASSWORD = $Password
    $env:CV_EVIDENCE_DIR = $evidence
    $env:CV_DESKTOP_HOST_CLOSE_SIGNAL = $closeSignal
    $env:CV_NODE_COMPLETION_SIGNAL = $nodeCompletionSignal
    $nodeProcess = $null
    try {
        $nodeProcess = Start-Process -FilePath $nodeExe `
            -ArgumentList @("`"$nodeSmoke`"") `
            -WorkingDirectory $uiTests `
            -RedirectStandardOutput $nodeStdout `
            -RedirectStandardError $nodeStderr `
            -WindowStyle Hidden `
            -PassThru
        $deadline = [DateTime]::UtcNow.AddMinutes(10)
        $shutdownRequested = $false
        while (-not $nodeProcess.HasExited) {
            if (-not $shutdownRequested -and (Test-Path -LiteralPath $closeSignal -PathType Leaf)) {
                $requested = [ClearVisionReleaseSmoke.NativeWindow]::RequestClose(
                    [uint32]$DesktopProcess.Id)
                if (-not $requested) {
                    throw "Could not request coordinated close for Desktop Host PID $($DesktopProcess.Id)."
                }
                $shutdownRequested = $true
                if (-not $DesktopProcess.WaitForExit($shutdownRunnerTotalDeadlineSeconds * 1000)) {
                    Stop-Process -Id $DesktopProcess.Id -Force -ErrorAction SilentlyContinue
                    throw "Desktop Host did not complete its coordinated close/flush path within $shutdownRunnerTotalDeadlineSeconds seconds."
                }
                [pscustomobject]@{
                    ProcessId = $DesktopProcess.Id
                    ExitedAtUtc = [DateTime]::UtcNow.ToString("O")
                } | ConvertTo-Json -Compress | Set-Content -LiteralPath $closeAcknowledgement -Encoding utf8
            }
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "WebView2 smoke phase '$Phase' at scale $Scale exceeded 10 minutes."
            }
            Start-Sleep -Milliseconds 50
        }
        $nodeProcess.WaitForExit()
        $nodeProcess.Refresh()
        $nodeExitCode = $nodeProcess.ExitCode
        if ($null -eq $nodeExitCode -and (Test-Path -LiteralPath $nodeCompletionSignal -PathType Leaf)) {
            $nodeExitCode = 0
        }
        if ($nodeExitCode -ne 0) {
            throw "WebView2 smoke phase '$Phase' at scale $Scale failed with exit code $nodeExitCode."
        }
    } finally {
        if ($nodeProcess -and -not $nodeProcess.HasExited) {
            Stop-Process -Id $nodeProcess.Id -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $nodeStdout -PathType Leaf) {
            Get-Content -LiteralPath $nodeStdout
        }
        if (Test-Path -LiteralPath $nodeStderr -PathType Leaf) {
            Get-Content -LiteralPath $nodeStderr | Write-Error -ErrorAction Continue
        }
        Remove-Item -LiteralPath $closeSignal, $closeAcknowledgement, $nodeCompletionSignal -Force -ErrorAction SilentlyContinue
        Remove-Item Env:CV_SMOKE_TOKEN -ErrorAction SilentlyContinue
        Remove-Item Env:CV_SMOKE_USER -ErrorAction SilentlyContinue
        Remove-Item Env:CV_SMOKE_USERNAME -ErrorAction SilentlyContinue
        Remove-Item Env:CV_SMOKE_PASSWORD -ErrorAction SilentlyContinue
        Remove-Item Env:CV_DESKTOP_HOST_CLOSE_SIGNAL -ErrorAction SilentlyContinue
        Remove-Item Env:CV_NODE_COMPLETION_SIGNAL -ErrorAction SilentlyContinue
    }
}

$runs = if ($SingleRun) {
    @(
        @{ Scale = $Scale; Port = $CdpPort; Phase = $Phase; Name = $RunName }
    )
} else {
    @(
        @{ Scale = 1.0; Port = 9323; Phase = "full"; Name = "dpi-100-full" },
        @{ Scale = 1.0; Port = 9324; Phase = "reopen"; Name = "dpi-100-reopen" },
        @{ Scale = 1.25; Port = 9325; Phase = "layout"; Name = "dpi-125-layout" },
        @{ Scale = 1.5; Port = 9326; Phase = "layout"; Name = "dpi-150-layout" }
    )
}

$shutdownDiagnostics = [System.Collections.Generic.List[object]]::new()
try {
    foreach ($run in $runs) {
        $process = $null
        try {
            $process = Start-DesktopHost -CdpPort $run.Port -Scale $run.Scale -RunName $run.Name
            Invoke-WebViewSmoke `
                -CdpPort $run.Port `
                -Scale $run.Scale `
                -Phase $run.Phase `
                -RunName $run.Name `
                -DesktopProcess $process
        } finally {
            if ($process) { Stop-DesktopHost -Process $process }
        }
        $shutdownPath = Join-Path $hostLogs "$($run.Name)-shutdown.jsonl"
        $shutdownRecord = Read-ShutdownDiagnostics -Path $shutdownPath
        $shutdownDiagnostics.Add($shutdownRecord)
        if (-not [bool]$shutdownRecord.passed) {
            throw "Desktop Host shutdown diagnostics did not pass for run '$($run.Name)': $shutdownPath"
        }
    }
} finally {
    Restore-ProcessEnvironment
    if (($useIsolatedAuth -or -not [string]::IsNullOrWhiteSpace($DatabasePath)) -and
        -not $KeepDatabase) {
        foreach ($databaseArtifact in @(
            $isolatedDatabase,
            ($isolatedDatabase + "-shm"),
            ($isolatedDatabase + "-wal"))) {
            Remove-ItemWithRetry -LiteralPath $databaseArtifact
        }
    }
    Remove-RepositoryTemporaryDirectory -Path $webView2UserDataRoot
    Remove-RepositoryTemporaryDirectory -Path $conversationRoot
    Remove-RepositoryTemporaryDirectory -Path $agentRunRoot
    Remove-RepositoryTemporaryDirectory -Path $handoffArtifactRoot
}

[pscustomobject]@{
    Succeeded = $true
    EvidenceDirectory = $evidence
    IsolationRoot = $isolationRoot
    ShutdownDeadlineContract = [pscustomobject]@{
        flushSeconds = $shutdownFlushDeadlineSeconds
        webViewDisposeSeconds = $shutdownWebViewDisposeDeadlineSeconds
        hostStopSeconds = $shutdownHostStopDeadlineSeconds
        processMarginSeconds = $shutdownProcessMarginSeconds
        runnerTotalSeconds = $shutdownRunnerTotalDeadlineSeconds
    }
    Runs = @($runs).Count
    DatabasePath = if ([string]::IsNullOrWhiteSpace($DatabasePath)) { $null } else { $isolatedDatabase }
    DatabaseKept = [bool]$KeepDatabase
    DatabaseReused = [bool]$ReuseDatabase
    InitialAdminSetupAllowed = [bool]$AllowInitialAdminSetup
    AuthenticationDeferredToScenario = [bool]$DeferAuthToScenario
    UnattendedShutdown = [bool]$UnattendedShutdown
    ShutdownDiagnostics = @($shutdownDiagnostics)
    CompletedAtUtc = [DateTime]::UtcNow.ToString("O")
} | ConvertTo-Json -Depth 4
