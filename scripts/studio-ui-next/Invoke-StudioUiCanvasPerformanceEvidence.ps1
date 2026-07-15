[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateSet("debug", "publish")]
    [string]$RuntimeKind = "debug",
    [ValidateSet("f01", "f02")]
    [string]$EvidencePhase = "f01",
    [string]$DesktopExecutablePath,
    [string]$NodeExecutablePath,
    [string]$RunName,
    [string]$EvidenceDirectory,
    [string]$RuntimeDirectory,
    [int]$GroupCount = 1,
    [int]$Warmups = 2,
    [int]$FormalSamples = 5,
    [int]$BaseWebPort = 5200,
    [int]$BaseCdpPort = 9523,
    [double]$Scale = 1.0,
    [switch]$SanitizeDesktopPath,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($GroupCount -lt 1) {
    throw "GroupCount must be at least one."
}
if ($Warmups -lt 2) {
    throw "Warmups must be at least two."
}
if ($FormalSamples -lt 5) {
    throw "FormalSamples must be at least five."
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot "../.."))
$singleRun = Join-Path $scriptRoot "Invoke-StudioUiWebView2Evidence.ps1"
$analyzer = Join-Path $scriptRoot "Measure-StudioUiCanvasPerformance.ps1"
$nodeScenario = Join-Path $repoRoot (
    "ClearVision.Product/tests/ClearVision.Product.UI.Tests/" +
    "tests/e2e/studio-ui-next/studio-ui-canvas-performance.cjs")
$nodeExe = if ([string]::IsNullOrWhiteSpace($NodeExecutablePath)) {
    (Get-Command node.exe -ErrorAction Stop).Source
} else {
    [System.IO.Path]::GetFullPath($NodeExecutablePath)
}
if ([string]::IsNullOrWhiteSpace($RunName)) {
    $RunName = "canvas-perf-{0}" -f [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss-fff")
}
$RunName = ($RunName -replace '[^A-Za-z0-9_.-]+', '-').Trim('-')
if ([string]::IsNullOrWhiteSpace($RunName)) {
    throw "RunName must contain at least one safe filename character."
}

$relativeRoot = if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    ".tmp/studio-ui-next/$EvidencePhase/performance/$RunName"
} else {
    $EvidenceDirectory.Replace('\', '/')
}
if ([System.IO.Path]::IsPathRooted($relativeRoot)) {
    throw "EvidenceDirectory must be repository-relative."
}
$evidenceRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $relativeRoot))
$allowedRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ".tmp/studio-ui-next"))
$allowedPrefix = $allowedRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $evidenceRoot.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Performance evidence must remain under .tmp/studio-ui-next."
}
if (Test-Path -LiteralPath $evidenceRoot) {
    throw "Performance evidence root already exists; use a unique RunName: $evidenceRoot"
}

$runtimeRoot = if ([string]::IsNullOrWhiteSpace($RuntimeDirectory)) {
    Join-Path $repoRoot ".tmp/studio-ui-next/$EvidencePhase/runtime/$RunName"
} else {
    [System.IO.Path]::GetFullPath($RuntimeDirectory)
}
$runtimeParent = [System.IO.Path]::GetFullPath((Split-Path -Parent $runtimeRoot))
$runtimeVolumeRoot = [System.IO.Path]::GetPathRoot($runtimeParent)
if ([string]::Equals(
    $runtimeParent.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar),
    $runtimeVolumeRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar),
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "RuntimeDirectory must be nested below a dedicated temporary parent."
}
if (Test-Path -LiteralPath $runtimeRoot) {
    throw "RuntimeDirectory already exists; use an isolated path: $runtimeRoot"
}
$runtimePrefix = $runtimeParent.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $runtimeRoot.StartsWith(
    $runtimePrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "RuntimeDirectory must be a child of its dedicated temporary parent."
}

$performanceEnvironment = [ordered]@{
    "CV_STUDIO_UI_PERF_GROUP" = $null
    "CV_STUDIO_UI_PERF_WARMUPS" = [string]$Warmups
    "CV_STUDIO_UI_PERF_SAMPLES" = [string]$FormalSamples
}
$previousEnvironment = @{}
foreach ($entry in $performanceEnvironment.GetEnumerator()) {
    $previousEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process")
}

function Restore-PerformanceEnvironment {
    foreach ($entry in $performanceEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable(
            $entry.Key,
            $previousEnvironment[$entry.Key],
            "Process")
    }
}

$runError = $null
$runtimeCleanupError = $null
$hasBuilt = [bool]$NoBuild
try {
    [Environment]::SetEnvironmentVariable(
        "CV_STUDIO_UI_PERF_WARMUPS",
        [string]$Warmups,
        "Process")
    [Environment]::SetEnvironmentVariable(
        "CV_STUDIO_UI_PERF_SAMPLES",
        [string]$FormalSamples,
        "Process")

    for ($groupIndex = 1; $groupIndex -le $GroupCount; $groupIndex += 1) {
        $groupId = "{0}-g{1:D2}" -f $RunName, $groupIndex
        [Environment]::SetEnvironmentVariable(
            "CV_STUDIO_UI_PERF_GROUP",
            $groupId,
            "Process")
        $portOffset = ($groupIndex - 1) * 4
        $legacyParameters = @{
            Expectation = "legacy"
            EvidencePhase = $EvidencePhase
            Configuration = $Configuration
            RuntimeKind = $RuntimeKind
            NodeExecutablePath = $nodeExe
            NodeScenarioPath = $nodeScenario
            RunName = "$groupId-legacy"
            EvidenceDirectory = "$relativeRoot/$groupId/legacy/evidence"
            WebPort = $BaseWebPort + $portOffset
            CdpPort = $BaseCdpPort + $portOffset
            Scale = $Scale
            RuntimeDirectory = Join-Path $runtimeRoot "$groupId/legacy"
        }
        $studioParameters = @{
            Expectation = "studio-canvas"
            EvidencePhase = $EvidencePhase
            Configuration = $Configuration
            RuntimeKind = $RuntimeKind
            NodeExecutablePath = $nodeExe
            NodeScenarioPath = $nodeScenario
            RunName = "$groupId-studio"
            EvidenceDirectory = "$relativeRoot/$groupId/studio/evidence"
            WebPort = $BaseWebPort + $portOffset + 1
            CdpPort = $BaseCdpPort + $portOffset + 1
            Scale = $Scale
            RuntimeDirectory = Join-Path $runtimeRoot "$groupId/studio"
            NoBuild = $true
        }
        if (-not [string]::IsNullOrWhiteSpace($DesktopExecutablePath)) {
            $legacyParameters["DesktopExecutablePath"] = $DesktopExecutablePath
            $studioParameters["DesktopExecutablePath"] = $DesktopExecutablePath
        }
        if ($SanitizeDesktopPath) {
            $legacyParameters["SanitizeDesktopPath"] = $true
            $studioParameters["SanitizeDesktopPath"] = $true
        }
        if ($hasBuilt) {
            $legacyParameters["NoBuild"] = $true
        }

        & $singleRun @legacyParameters
        $hasBuilt = $true
        & $singleRun @studioParameters
    }
} catch {
    $runError = $_
} finally {
    Restore-PerformanceEnvironment
    if (Test-Path -LiteralPath $runtimeRoot) {
        try {
            Remove-Item -LiteralPath $runtimeRoot -Recurse -Force
        } catch {
            $runtimeCleanupError = $_
        }
    }
}

$summaryPath = Join-Path $evidenceRoot "studio-ui-canvas-performance-summary.json"
$analysisError = $null
if (Test-Path -LiteralPath $evidenceRoot -PathType Container) {
    try {
        & $analyzer -EvidenceDirectory $evidenceRoot -OutputPath $summaryPath
    } catch {
        $analysisError = $_
    }
}

if ($runError) {
    throw $runError
}
if ($runtimeCleanupError) {
    throw $runtimeCleanupError
}
if ($analysisError) {
    throw $analysisError
}
if (-not (Test-Path -LiteralPath $summaryPath -PathType Leaf)) {
    throw "Canvas performance summary was not produced: $summaryPath"
}
$summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
if ($summary.decision -eq "BLOCKED") {
    throw "BLOCKED_CANVAS_PERFORMANCE. See $summaryPath"
}

[pscustomobject]@{
    Succeeded = $true
    Decision = $summary.decision
    ComparisonGroups = $summary.completeComparisonGroupCount
    EvidenceDirectory = $evidenceRoot
    SummaryPath = $summaryPath
    CompletedAtUtc = [DateTime]::UtcNow.ToString("O")
} | ConvertTo-Json -Depth 4
