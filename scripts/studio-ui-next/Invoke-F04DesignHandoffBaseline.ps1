[CmdletBinding()]
param(
    [string]$SourceSha,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$uiTestsRoot = Join-Path $repositoryRoot "ClearVision.Product\tests\ClearVision.Product.UI.Tests"
$catalogPath = Join-Path $uiTestsRoot "tests\e2e\studio-ui-next\f04-design-handoff-baseline.mjs"
$allowedEvidenceRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot ".tmp\studio-ui-next\f04"))
$nodeExecutable = @(Get-Command node.exe -CommandType Application -ErrorAction Stop)[0].Source
$npxExecutable = @(Get-Command npx.cmd -CommandType Application -ErrorAction Stop)[0].Source

if ([string]::IsNullOrWhiteSpace($SourceSha)) {
    $SourceSha = (& git -C $repositoryRoot rev-parse HEAD).Trim()
}
if ($SourceSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw "SourceSha must contain a 40-character commit SHA."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $allowedEvidenceRoot (
        "design-handoff-baseline-" + $SourceSha.Substring(0, 12))
}
$evidenceRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$allowedPrefix = $allowedEvidenceRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
if (-not $evidenceRoot.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must remain under $allowedEvidenceRoot"
}

New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
$grep = (& $nodeExecutable $catalogPath --grep).Trim()
if ([string]::IsNullOrWhiteSpace($grep)) {
    throw "The F04 design handoff catalog returned an empty Playwright grep expression."
}

$repositoryPrefix = $repositoryRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
$relativeEvidence = $evidenceRoot.Substring($repositoryPrefix.Length).Replace('\', '/')
$previousScenario = $env:CV_UI_SCENARIO
$previousEvidenceDirectory = $env:CV_F04_VISUAL_EVIDENCE_DIR
$previousSourceSha = $env:CV_F04_SOURCE_SHA
try {
    $env:CV_UI_SCENARIO = "studio-ui-next"
    $env:CV_F04_VISUAL_EVIDENCE_DIR = $relativeEvidence
    $env:CV_F04_SOURCE_SHA = $SourceSha.ToLowerInvariant()

    Push-Location $uiTestsRoot
    try {
        & $npxExecutable playwright test `
            tests/e2e/studio-ui-next/f03-workspace.spec.ts `
            tests/e2e/studio-ui-next/f04-project-lifecycle.spec.ts `
            --project=chromium `
            --workers=1 `
            --reporter=list `
            --grep $grep
        if ($LASTEXITCODE -ne 0) {
            throw "F04 design handoff Playwright capture failed with exit code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }

    & $nodeExecutable $catalogPath --validate $evidenceRoot $SourceSha
    if ($LASTEXITCODE -ne 0) {
        throw "F04 design handoff evidence validation failed with exit code $LASTEXITCODE."
    }
} finally {
    $env:CV_UI_SCENARIO = $previousScenario
    $env:CV_F04_VISUAL_EVIDENCE_DIR = $previousEvidenceDirectory
    $env:CV_F04_SOURCE_SHA = $previousSourceSha
}

Write-Host "F04 design handoff baseline PASS: $evidenceRoot"
