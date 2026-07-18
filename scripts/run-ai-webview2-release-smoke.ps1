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
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
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
$hostLogs = if ([string]::IsNullOrWhiteSpace($HostLogDirectory)) {
    Join-Path $repoRoot ".tmp/ai-webview2-release-host-logs"
} else {
    [System.IO.Path]::GetFullPath($HostLogDirectory)
}
$runtime = "net8.0-windows/win-x64"
$exe = if ([string]::IsNullOrWhiteSpace($DesktopExecutablePath)) {
    Join-Path $repoRoot "ClearVision.Product/src/ClearVision.Product.Desktop/bin/$Configuration/$runtime/ClearVision.Product.Desktop.exe"
} else {
    [System.IO.Path]::GetFullPath($DesktopExecutablePath)
}
$useIsolatedAuth = [string]::IsNullOrWhiteSpace($Password)
$isolatedAuthDirectory = Join-Path $repoRoot ".tmp/ai-webview2-release-auth"
$isolatedDatabase = if ([string]::IsNullOrWhiteSpace($DatabasePath)) {
    Join-Path $isolatedAuthDirectory "vision.db"
} else {
    [System.IO.Path]::GetFullPath($DatabasePath)
}
$runtimeIsolationRoot = Join-Path $repoRoot ".tmp/ai-webview2-release-runtime"
$runtimeCleanupBoundary = if ([string]::IsNullOrWhiteSpace($RuntimeCleanupRoot)) {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot ".tmp"))
} else {
    [System.IO.Path]::GetFullPath($RuntimeCleanupRoot)
}
$webView2UserDataRoot = if ([string]::IsNullOrWhiteSpace($WebView2UserDataDirectory)) {
    Join-Path $runtimeIsolationRoot "webview2"
} else {
    [System.IO.Path]::GetFullPath($WebView2UserDataDirectory)
}
$conversationRoot = if ([string]::IsNullOrWhiteSpace($ConversationStoreRoot)) {
    Join-Path $runtimeIsolationRoot "conversation"
} else {
    [System.IO.Path]::GetFullPath($ConversationStoreRoot)
}
$agentRunRoot = if ([string]::IsNullOrWhiteSpace($AgentRunStoreRoot)) {
    Join-Path $runtimeIsolationRoot "agent-runs"
} else {
    [System.IO.Path]::GetFullPath($AgentRunStoreRoot)
}

$environmentNames = @(
    "Database__Path",
    "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
    "CV_DESKTOP_HTTP_PORT",
    "CV_WEBVIEW2_USER_DATA_FOLDER",
    "CV_CONVERSATION_STORE_ROOT",
    "CV_AGENT_RUN_EVENT_STORE",
    "CV_DESKTOP_LOG_PATH",
    "CV_CDP_PORT",
    "CV_WEB_PORT",
    "CV_DPI_SCALE",
    "CV_SMOKE_PHASE",
    "CV_SMOKE_TOKEN",
    "CV_SMOKE_USER",
    "CV_SMOKE_USERNAME",
    "CV_SMOKE_PASSWORD",
    "CV_EVIDENCE_DIR",
    "PATH"
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

function Set-ProcessEnvironment {
    param([string]$Name, [string]$Value)
    [Environment]::SetEnvironmentVariable($Name, $Value, "Process")
}

function Restore-ProcessEnvironment {
    foreach ($name in $environmentNames) {
        $value = $previousEnvironment[$name]
        if ($null -eq $value) {
            [Environment]::SetEnvironmentVariable($name, $null, "Process")
            continue
        }
        [Environment]::SetEnvironmentVariable(
            $name,
            [string]$value,
            "Process")
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
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return
    }

    $target = [System.IO.Path]::GetFullPath($Path)
    $temporaryPrefix = $runtimeCleanupBoundary.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $target.StartsWith($temporaryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    Remove-ItemWithRetry -LiteralPath $target -Recurse
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

Set-ProcessEnvironment -Name "CV_DESKTOP_HTTP_PORT" -Value ([string]$WebPort)
Set-ProcessEnvironment -Name "CV_CONVERSATION_STORE_ROOT" -Value $conversationRoot
Set-ProcessEnvironment -Name "CV_AGENT_RUN_EVENT_STORE" -Value $agentRunRoot

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
    $runUserData = Join-Path $webView2UserDataRoot $RunName
    New-Item -ItemType Directory -Force -Path $runUserData | Out-Null
    Set-ProcessEnvironment -Name "CV_WEBVIEW2_USER_DATA_FOLDER" -Value $runUserData
    Set-ProcessEnvironment -Name "CV_DESKTOP_LOG_PATH" -Value (Join-Path $hostLogs "$RunName-desktop.log")
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
    if (-not $Process.WaitForExit(15000)) {
        Stop-Process -Id $Process.Id -Force
        throw "Desktop Host did not complete its close/flush path within 15 seconds."
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
    param([int]$CdpPort, [double]$Scale, [string]$Phase)
    $login = if ($DeferAuthToScenario) { $null } else { Get-AuthSession }
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
    Push-Location $uiTests
    try {
        & $nodeExe $nodeSmoke
        if ($LASTEXITCODE -ne 0) { throw "WebView2 smoke phase '$Phase' at scale $Scale failed with exit code $LASTEXITCODE." }
    } finally {
        Pop-Location
        Remove-Item Env:CV_SMOKE_TOKEN -ErrorAction SilentlyContinue
        Remove-Item Env:CV_SMOKE_USER -ErrorAction SilentlyContinue
        Remove-Item Env:CV_SMOKE_USERNAME -ErrorAction SilentlyContinue
        Remove-Item Env:CV_SMOKE_PASSWORD -ErrorAction SilentlyContinue
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

try {
    foreach ($run in $runs) {
        $process = $null
        try {
            $process = Start-DesktopHost -CdpPort $run.Port -Scale $run.Scale -RunName $run.Name
            Invoke-WebViewSmoke -CdpPort $run.Port -Scale $run.Scale -Phase $run.Phase
        } finally {
            if ($process) { Stop-DesktopHost -Process $process }
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
}

[pscustomobject]@{
    Succeeded = $true
    EvidenceDirectory = $evidence
    Runs = @($runs).Count
    DatabasePath = if ([string]::IsNullOrWhiteSpace($DatabasePath)) { $null } else { $isolatedDatabase }
    DatabaseKept = [bool]$KeepDatabase
    DatabaseReused = [bool]$ReuseDatabase
    InitialAdminSetupAllowed = [bool]$AllowInitialAdminSetup
    AuthenticationDeferredToScenario = [bool]$DeferAuthToScenario
    CompletedAtUtc = [DateTime]::UtcNow.ToString("O")
} | ConvertTo-Json -Depth 4
