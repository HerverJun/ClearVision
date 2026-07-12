param(
    [string]$Username = "admin",
    [string]$Password = $env:CV_SMOKE_PASSWORD,
    [string]$Configuration = "Debug",
    [string]$EvidenceDirectory = "quality/evidence/ai-webview2-release",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$project = Join-Path $repoRoot "ClearVision.Product/src/ClearVision.Product.Desktop/ClearVision.Product.Desktop.csproj"
$uiTests = Join-Path $repoRoot "ClearVision.Product/tests/ClearVision.Product.UI.Tests"
$nodeSmoke = Join-Path $uiTests "tests/e2e/ai-webview2-release-smoke.cjs"
$nodeExe = (Get-Command node.exe -ErrorAction Stop).Source
$evidence = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $EvidenceDirectory))
$hostLogs = Join-Path $repoRoot ".tmp/ai-webview2-release-host-logs"
$runtime = "net8.0-windows/win-x64"
$exe = Join-Path $repoRoot "ClearVision.Product/src/ClearVision.Product.Desktop/bin/$Configuration/$runtime/ClearVision.Product.Desktop.exe"
$useIsolatedAuth = [string]::IsNullOrWhiteSpace($Password)
$isolatedAuthDirectory = Join-Path $repoRoot ".tmp/ai-webview2-release-auth"
$isolatedDatabase = Join-Path $isolatedAuthDirectory "vision.db"
$previousDatabasePath = $env:Database__Path

if ($useIsolatedAuth) {
    New-Item -ItemType Directory -Force -Path $isolatedAuthDirectory | Out-Null
    Get-ChildItem -LiteralPath $isolatedAuthDirectory -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @('vision.db', 'vision.db-shm', 'vision.db-wal') } |
        Remove-Item -Force
    $randomBytes = New-Object byte[] 18
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($randomBytes) } finally { $random.Dispose() }
    $Password = ([Convert]::ToBase64String($randomBytes) + "Aa1!")
    $env:Database__Path = $isolatedDatabase
}

New-Item -ItemType Directory -Force -Path $evidence | Out-Null
New-Item -ItemType Directory -Force -Path $hostLogs | Out-Null

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

        public static bool RequestClose(uint processId) {
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
    $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$CdpPort --remote-allow-origins=* --force-device-scale-factor=$Scale"
    $process = Start-Process -FilePath $exe `
        -WorkingDirectory (Split-Path $exe) `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -WindowStyle Hidden `
        -PassThru
    Wait-HttpEndpoint -Uri "http://127.0.0.1:5000/api/auth/setup-status"
    Wait-HttpEndpoint -Uri "http://127.0.0.1:$CdpPort/json/version"
    return $process
}

function Stop-DesktopHost {
    param([System.Diagnostics.Process]$Process)
    if ($Process.HasExited) { return }
    $requested = [ClearVisionReleaseSmoke.NativeWindow]::RequestClose([uint32]$Process.Id)
    if (-not $requested) { throw "Could not locate the hidden WinForms window for PID $($Process.Id)." }
    if (-not $Process.WaitForExit(15000)) {
        Stop-Process -Id $Process.Id -Force
        throw "Desktop Host did not complete its close/flush path within 15 seconds."
    }
}

function Get-AuthSession {
    $body = @{ username = $Username; password = $Password } | ConvertTo-Json -Compress
    try {
        return Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:5000/api/auth/login" -ContentType "application/json" -Body $body
    } catch {
        if (-not $useIsolatedAuth) { throw }
        $setupBody = @{
            username = $Username
            password = $Password
            confirmPassword = $Password
        } | ConvertTo-Json -Compress
        return Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:5000/api/auth/setup-admin" -ContentType "application/json" -Body $setupBody
    }
}

function Invoke-WebViewSmoke {
    param([int]$CdpPort, [double]$Scale, [string]$Phase)
    $login = Get-AuthSession
    $env:CV_CDP_PORT = [string]$CdpPort
    $env:CV_DPI_SCALE = [string]$Scale
    $env:CV_SMOKE_PHASE = $Phase
    $env:CV_SMOKE_TOKEN = [string]$login.token
    $env:CV_SMOKE_USER = ($login.user | ConvertTo-Json -Compress)
    $env:CV_EVIDENCE_DIR = $evidence
    Push-Location $uiTests
    try {
        & $nodeExe $nodeSmoke
        if ($LASTEXITCODE -ne 0) { throw "WebView2 smoke phase '$Phase' at scale $Scale failed with exit code $LASTEXITCODE." }
    } finally {
        Pop-Location
        Remove-Item Env:CV_SMOKE_TOKEN -ErrorAction SilentlyContinue
        Remove-Item Env:CV_SMOKE_USER -ErrorAction SilentlyContinue
    }
}

$runs = @(
    @{ Scale = 1.0; Port = 9323; Phase = "full"; Name = "dpi-100-full" },
    @{ Scale = 1.0; Port = 9324; Phase = "reopen"; Name = "dpi-100-reopen" },
    @{ Scale = 1.25; Port = 9325; Phase = "layout"; Name = "dpi-125-layout" },
    @{ Scale = 1.5; Port = 9326; Phase = "layout"; Name = "dpi-150-layout" }
)

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
    $env:Database__Path = $previousDatabasePath
    if ($useIsolatedAuth) {
        Get-ChildItem -LiteralPath $isolatedAuthDirectory -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -in @('vision.db', 'vision.db-shm', 'vision.db-wal') } |
            Remove-Item -Force
    }
}

[pscustomobject]@{
    Succeeded = $true
    EvidenceDirectory = $evidence
    Runs = $runs.Count
    CompletedAtUtc = [DateTime]::UtcNow.ToString("O")
} | ConvertTo-Json -Depth 4
