param(
    [string]$ReportDirectory,
    [switch]$NoBuild,
    [switch]$FailOnWarning,
    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$dotnetShim = Join-Path $scriptRoot "dotnet.ps1"
$project = Join-Path $repoRoot "quality\tools\TestGovernanceRunner\TestGovernanceRunner.csproj"
$effectiveReportDirectory = if ([string]::IsNullOrWhiteSpace($ReportDirectory)) {
    Join-Path $repoRoot ".tmp\test-governance"
}
else {
    $ReportDirectory
}

$arguments = @(
    "run",
    "--project", $project
)
if ($NoBuild) {
    $arguments += "--no-build"
}
$arguments += @(
    "--",
    "--repo-root", $repoRoot,
    "--report-directory", $effectiveReportDirectory
)
if ($FailOnWarning) {
    $arguments += "--fail-on-warning"
}

& $dotnetShim @arguments
$exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
$global:LASTEXITCODE = $exitCode
if ($ReturnExitCode) { return }
exit $exitCode
