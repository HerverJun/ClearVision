[CmdletBinding()]
param(
    [string]$BaselineSha,
    [switch]$ReportOnly,
    [string]$JsonOutputPath,
    [string]$MarkdownOutputPath,
    [string]$SummaryJsonOutputPath
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$fixedDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts/operator-audit"))
$fixedPaths = @{
    Json = [System.IO.Path]::GetFullPath((Join-Path $fixedDirectory "operator-audit.json"))
    Markdown = [System.IO.Path]::GetFullPath((Join-Path $fixedDirectory "operator-audit.md"))
    Summary = [System.IO.Path]::GetFullPath((Join-Path $fixedDirectory "operator-audit-summary.json"))
}

foreach ($requested in @($JsonOutputPath, $MarkdownOutputPath, $SummaryJsonOutputPath)) {
    if ([string]::IsNullOrWhiteSpace($requested)) {
        continue
    }

    $candidate = if ([System.IO.Path]::IsPathRooted($requested)) {
        $requested
    }
    else {
        Join-Path $repoRoot $requested
    }
    $resolved = [System.IO.Path]::GetFullPath($candidate)
    if ($fixedPaths.Values -notcontains $resolved) {
        throw "The read-only audit only writes the three files under artifacts/operator-audit. Requested: $requested"
    }
}

$runnerProject = Join-Path $repoRoot "quality/tools/OperatorLibraryReadOnlyAuditRunner/OperatorLibraryReadOnlyAuditRunner.csproj"
$runnerArguments = @(
    "--repo-root", $repoRoot
)
if (-not [string]::IsNullOrWhiteSpace($BaselineSha)) {
    $runnerArguments += @("--source-commit-sha", $BaselineSha)
}
if ($ReportOnly) {
    $runnerArguments += "--report-only"
}

& dotnet run --project $runnerProject --configuration Release -p:NoWarn=MSB3277 -- @runnerArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

foreach ($path in $fixedPaths.Values) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Audit output is missing: $path"
    }
}
