param(
    [string]$ReportsRoot = "quality/evals/reports",
    [Alias("MaxJsonMB")]
    [double]$MaxReportMB = 1,
    [string]$AllowlistPath = "quality/evals/reports/quality-report-size-allowlist.txt",
    [string[]]$ReportExtensions = @("*.json", "*.csv"),
    [int]$MinimumReportFiles = 1
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedReportsRoot = if ([System.IO.Path]::IsPathRooted($ReportsRoot)) {
    $ReportsRoot
} else {
    Join-Path $repoRoot $ReportsRoot
}

$resolvedAllowlistPath = if ([System.IO.Path]::IsPathRooted($AllowlistPath)) {
    $AllowlistPath
} else {
    Join-Path $repoRoot $AllowlistPath
}

function Convert-ToRepoRelativePath {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Replace('\', '/')
    }

    return $fullPath.Substring($fullRoot.Length).Replace('\', '/')
}

$allowed = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
if (Test-Path -LiteralPath $resolvedAllowlistPath) {
    foreach ($line in Get-Content -LiteralPath $resolvedAllowlistPath) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) {
            continue
        }

        [void]$allowed.Add($trimmed.Replace('\', '/'))
    }
}

if (-not (Test-Path -LiteralPath $resolvedReportsRoot)) {
    throw "Quality report directory not found: $ReportsRoot"
}

if ($MaxReportMB -le 0) {
    throw "MaxReportMB must be greater than 0."
}

if ($MinimumReportFiles -lt 0) {
    throw "MinimumReportFiles must be greater than or equal to 0."
}

if ($ReportExtensions.Count -eq 0) {
    throw "At least one report extension must be configured."
}

$maxBytes = [int64]($MaxReportMB * 1MB)
$failures = New-Object System.Collections.Generic.List[string]

$reportFiles = foreach ($extension in $ReportExtensions) {
    Get-ChildItem -LiteralPath $resolvedReportsRoot -Filter $extension -File -Recurse
}

$uniqueReportFiles = @($reportFiles | Sort-Object FullName -Unique)
if ($uniqueReportFiles.Count -lt $MinimumReportFiles) {
    throw "Quality report size guard found $($uniqueReportFiles.Count) report file(s); expected at least $MinimumReportFiles under $ReportsRoot."
}

$uniqueReportFiles |
    ForEach-Object {
        if ($_.Length -le $maxBytes) {
            return
        }

        $relative = Convert-ToRepoRelativePath $_.FullName
        if ($allowed.Contains($relative)) {
            return
        }

        $sizeMb = [Math]::Round($_.Length / 1MB, 2)
        $failures.Add("$relative is $sizeMb MB; limit is $MaxReportMB MB. Add a summary file or explicitly allowlist the legacy file.")
    }

if ($failures.Count -gt 0) {
    Write-Host "Quality report size guard failed:"
    foreach ($failure in $failures) {
        Write-Host " - $failure"
    }

    exit 1
}

Write-Host "Quality report size guard passed. Files=$($uniqueReportFiles.Count), MaxReportMB=$MaxReportMB, Extensions=$($ReportExtensions -join ','), Allowlisted=$($allowed.Count)"
