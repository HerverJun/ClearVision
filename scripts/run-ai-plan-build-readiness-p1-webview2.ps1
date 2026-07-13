param(
    [string]$Configuration = "Debug",
    [string]$EvidenceDirectory = "quality/evidence/ai-plan-build-readiness-p1/after",
    [int]$CdpPort = 9332,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$project = Join-Path $repoRoot "ClearVision.Product/src/ClearVision.Product.Desktop/ClearVision.Product.Desktop.csproj"
$uiTests = Join-Path $repoRoot "ClearVision.Product/tests/ClearVision.Product.UI.Tests"
$scenario = Join-Path $uiTests "tests/e2e/ai-plan-build-readiness-p1-webview2.cjs"
$runtime = "net8.0-windows/win-x64"
$exe = Join-Path $repoRoot "ClearVision.Product/src/ClearVision.Product.Desktop/bin/$Configuration/$runtime/ClearVision.Product.Desktop.exe"
$evidence = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $EvidenceDirectory))
$scratch = Join-Path $repoRoot ".tmp/ai-plan-build-readiness-p1-after-host"
$database = Join-Path $scratch "vision.db"
$stdout = Join-Path $scratch "stdout.log"
$stderr = Join-Path $scratch "stderr.log"
$previousDatabasePath = $env:Database__Path
$previousWebViewArguments = $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS
$previousPlanPlannerEnabled = $env:AI__VisionAgent__PlanPlanner__Enabled
$process = $null

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

try {
    New-Item -ItemType Directory -Force -Path $scratch, $evidence | Out-Null
    foreach ($name in @("vision.db", "vision.db-shm", "vision.db-wal")) {
        $target = Join-Path $scratch $name
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
    }

    if (-not $NoBuild) {
        & dotnet build $project -c $Configuration --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Desktop build failed with exit code $LASTEXITCODE." }
    }
    if (-not (Test-Path -LiteralPath $exe)) { throw "Desktop executable not found: $exe" }
    if (Get-Process ClearVision.Product.Desktop -ErrorAction SilentlyContinue) {
        throw "A ClearVision.Product.Desktop process is already running."
    }

    $env:Database__Path = $database
    # Keep this focused admission test deterministic while still exercising the real Plan fallback,
    # Readiness endpoint, Workspace reducer and WebView2 UI path.
    $env:AI__VisionAgent__PlanPlanner__Enabled = "false"
    $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$CdpPort --remote-allow-origins=* --force-device-scale-factor=1"
    $process = Start-Process -FilePath $exe `
        -WorkingDirectory (Split-Path $exe) `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -WindowStyle Hidden `
        -PassThru

    Wait-HttpEndpoint -Uri "http://127.0.0.1:5000/api/auth/setup-status"
    Wait-HttpEndpoint -Uri "http://127.0.0.1:$CdpPort/json/version"

    $bytes = New-Object byte[] 18
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($bytes) } finally { $random.Dispose() }
    $password = ([Convert]::ToBase64String($bytes) + "Aa1!")
    $setup = @{ username = "admin"; password = $password; confirmPassword = $password } | ConvertTo-Json -Compress
    $login = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:5000/api/auth/setup-admin" -ContentType "application/json" -Body $setup

    $env:CV_P1_CDP_PORT = [string]$CdpPort
    $env:CV_P1_TOKEN = [string]$login.token
    $env:CV_P1_USER = ($login.user | ConvertTo-Json -Compress)
    $env:CV_P1_EVIDENCE_DIR = $evidence
    Push-Location $uiTests
    try {
        & node.exe $scenario
        if ($LASTEXITCODE -ne 0) { throw "WebView2 P1 scenario failed with exit code $LASTEXITCODE." }
    } finally {
        Pop-Location
    }
} finally {
    Remove-Item Env:CV_P1_CDP_PORT, Env:CV_P1_TOKEN, Env:CV_P1_USER, Env:CV_P1_EVIDENCE_DIR -ErrorAction SilentlyContinue
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(5000) | Out-Null
    }
    if ($null -eq $previousDatabasePath) { Remove-Item Env:Database__Path -ErrorAction SilentlyContinue }
    else { $env:Database__Path = $previousDatabasePath }
    if ($null -eq $previousWebViewArguments) { Remove-Item Env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS -ErrorAction SilentlyContinue }
    else { $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = $previousWebViewArguments }
    if ($null -eq $previousPlanPlannerEnabled) { Remove-Item Env:AI__VisionAgent__PlanPlanner__Enabled -ErrorAction SilentlyContinue }
    else { $env:AI__VisionAgent__PlanPlanner__Enabled = $previousPlanPlannerEnabled }
}
