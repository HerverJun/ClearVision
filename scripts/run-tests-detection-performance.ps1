param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [ValidateSet("auto", "standard", "acceptance")]
    [string]$GateProfile = "auto",

    [string]$TightenFromUtc,

    [string]$Configuration,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [string]$ResultsDirectory,

    [string]$LogFileName,

    [int]$MinimumTotalTests = 1,

    [int]$MinimumReportEntries = 15,

    [string]$ReportDirectory,

    [switch]$ValidateReportOnly,

    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$runner = Join-Path $scriptRoot "run-dotnet-test-serial.ps1"
$project = Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.Tests\ClearVision.Product.Tests.csproj"
$gateConfiguration = Get-Content -LiteralPath (Join-Path $repoRoot "quality\test-gates.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$gateDefinition = @($gateConfiguration.gates | Where-Object { $_.name -ceq "detection-performance" })
if ($gateDefinition.Count -ne 1) {
    throw "Expected exactly one detection-performance gate in quality/test-gates.json; found $($gateDefinition.Count)."
}
$defaultResultsDirectory = Join-Path $repoRoot ".tmp\test_results\detection-performance"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$defaultLogFileName = "detection-performance-$timestamp.trx"
$expectedPerformanceEntries = @(
    "AngleMeasurement",
    "CaliperTool",
    "CircleMeasurement",
    "ContourMeasurement",
    "GapMeasurement",
    "GeoMeasurement",
    "GeometricTolerance",
    "HistogramAnalysis",
    "LineLineDistance",
    "LineMeasurement",
    "MeasureDistance",
    "PixelStatistics",
    "PointLineDistance",
    "SharpnessEvaluation",
    "WidthMeasurement"
)

function Resolve-RepoRelativePath {
    param(
        [string]$PathValue
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $PathValue
    }

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PathValue))
}

function Resolve-DetectionPerformanceReportDirectory {
    if (-not [string]::IsNullOrWhiteSpace($env:CV_DETECTION_PERF_REPORT_DIR)) {
        return $env:CV_DETECTION_PERF_REPORT_DIR
    }

    if (-not [string]::IsNullOrWhiteSpace($env:CV_PERF_REPORT_DIR)) {
        return $env:CV_PERF_REPORT_DIR
    }

    return Join-Path $repoRoot "ClearVision.Product\test_results"
}

function Test-PerformanceReportArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReportDirectory,

        [Parameter(Mandatory = $true)]
        [datetime]$RunStartedAtUtc,

        [Parameter(Mandatory = $true)]
        [int]$MinimumEntries,

        [Parameter(Mandatory = $true)]
        [string[]]$ExpectedEntries,

        [switch]$SkipFreshnessCheck
    )

    $allowedClockSkewSeconds = 2
    $minimumWriteTimeUtc = if ($SkipFreshnessCheck) {
        [DateTime]::MinValue
    } else {
        $RunStartedAtUtc.AddSeconds(-$allowedClockSkewSeconds)
    }
    $expectedReports = @(
        (Join-Path $ReportDirectory "detection_performance_budget_report.md"),
        (Join-Path $ReportDirectory "detection_performance_budget_report.json")
    )

    foreach ($reportPath in $expectedReports) {
        if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
            throw "Expected detection performance report artifact was not written: $reportPath"
        }

        $report = Get-Item -LiteralPath $reportPath
        if (-not $SkipFreshnessCheck -and $report.LastWriteTimeUtc -lt $minimumWriteTimeUtc) {
            throw "Detection performance report artifact is stale: $reportPath (lastWriteUtc=$($report.LastWriteTimeUtc.ToString("o")), runStartedUtc=$($RunStartedAtUtc.ToString("o")))"
        }
    }

    $jsonPath = Join-Path $ReportDirectory "detection_performance_budget_report.json"
    try {
        $reportJson = Get-Content -LiteralPath $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "Unable to parse detection performance report JSON: $jsonPath. $($_.Exception.Message)"
    }

    if ($null -eq $reportJson.PSObject.Properties["Entries"] -or $null -eq $reportJson.Entries) {
        throw "Detection performance report JSON is missing Entries: $jsonPath"
    }

    $entries = @($reportJson.Entries)
    if ($entries.Count -lt $MinimumEntries) {
        throw "Detection performance report has $($entries.Count) entr(ies); expected at least $MinimumEntries."
    }

    $entryNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $entries) {
        $name = if ($null -ne $entry.PSObject.Properties["Name"]) { [string]$entry.Name } else { "" }
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw "Detection performance report contains an entry without Name."
        }

        if (-not $entryNames.Add($name)) {
            throw "Detection performance report contains duplicate entry Name: $name"
        }

        $status = if ($null -ne $entry.PSObject.Properties["Status"]) { [string]$entry.Status } else { "" }
        if (-not $status.Equals("PASS", [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Detection performance report entry '$name' is not PASS: $status"
        }

        foreach ($field in @("BudgetMs", "Scale", "AllowedMs", "MeanMs", "P95Ms", "P99Ms")) {
            if ($null -eq $entry.PSObject.Properties[$field]) {
                throw "Detection performance report entry '$name' is missing $field."
            }

            $value = [double]$entry.$field
            if ([double]::IsNaN($value) -or [double]::IsInfinity($value)) {
                throw "Detection performance report entry '$name' has non-finite $field."
            }

            if ($field -in @("BudgetMs", "Scale", "AllowedMs") -and $value -le 0) {
                throw "Detection performance report entry '$name' has non-positive ${field}: $value"
            }

            if ($field -in @("MeanMs", "P95Ms", "P99Ms") -and $value -lt 0) {
                throw "Detection performance report entry '$name' has negative ${field}: $value"
            }
        }
    }

    $missingEntries = @($ExpectedEntries | Where-Object { -not $entryNames.Contains($_) })
    if ($missingEntries.Count -gt 0) {
        throw "Detection performance report is missing expected entr(ies): $($missingEntries -join ', ')"
    }

    Write-Host "[detection-perf] Report artifact validation passed: $ReportDirectory (entries=$($entries.Count), minimum=$MinimumEntries)"
}

function Resolve-DetectionPerfGateProfile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RequestedProfile,

        [string]$TightenFromUtcInput
    )

    if ($RequestedProfile -in @("standard", "acceptance")) {
        return [PSCustomObject]@{
            Profile = $RequestedProfile
            Reason = "explicit parameter"
        }
    }

    $envProfile = $env:CV_DETECTION_PERF_GATE_PROFILE
    if ($envProfile -in @("standard", "acceptance")) {
        return [PSCustomObject]@{
            Profile = $envProfile
            Reason = "CV_DETECTION_PERF_GATE_PROFILE override"
        }
    }

    $effectiveTightenFrom = $TightenFromUtcInput
    if ([string]::IsNullOrWhiteSpace($effectiveTightenFrom)) {
        $effectiveTightenFrom = $env:CV_DETECTION_PERF_TIGHTEN_FROM_UTC
    }

    if (-not [string]::IsNullOrWhiteSpace($effectiveTightenFrom)) {
        $styles = [System.Globalization.DateTimeStyles]::AssumeUniversal -bor [System.Globalization.DateTimeStyles]::AdjustToUniversal
        $parsed = [DateTimeOffset]::MinValue
        if ([DateTimeOffset]::TryParse($effectiveTightenFrom, [System.Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$parsed)) {
            if ([DateTimeOffset]::UtcNow -ge $parsed) {
                return [PSCustomObject]@{
                    Profile = "acceptance"
                    Reason = "auto tightened by CV_DETECTION_PERF_TIGHTEN_FROM_UTC=$effectiveTightenFrom"
                }
            }
        }
        else {
            Write-Warning "[detection-perf] Invalid CV_DETECTION_PERF_TIGHTEN_FROM_UTC value '$effectiveTightenFrom'. Expected an ISO-8601 UTC timestamp (for example: 2026-05-01T00:00:00Z)."
        }
    }

    $ref = $env:GITHUB_REF
    if ($ref -eq "refs/heads/main" -or $ref -like "refs/tags/v*") {
        return [PSCustomObject]@{
            Profile = "acceptance"
            Reason = "auto tightened for main branch or release tag"
        }
    }

    return [PSCustomObject]@{
        Profile = "standard"
        Reason = "auto default"
    }
}

if (-not [string]::IsNullOrWhiteSpace($TightenFromUtc)) {
    $env:CV_DETECTION_PERF_TIGHTEN_FROM_UTC = $TightenFromUtc
}

$resolvedProfile = Resolve-DetectionPerfGateProfile -RequestedProfile $GateProfile -TightenFromUtcInput $TightenFromUtc
$effectiveGateProfile = $resolvedProfile.Profile

if ([string]::IsNullOrWhiteSpace($env:CV_DETECTION_PERF_BUDGET_SCALE)) {
    $env:CV_DETECTION_PERF_BUDGET_SCALE = if ($effectiveGateProfile -eq "acceptance") { "1.2" } else { "1.5" }
}

if ([string]::IsNullOrWhiteSpace($env:CV_DETECTION_PERF_WARMUP_ITERS)) {
    $env:CV_DETECTION_PERF_WARMUP_ITERS = "5"
}

if ([string]::IsNullOrWhiteSpace($env:CV_DETECTION_PERF_MEASURE_ITERS)) {
    $env:CV_DETECTION_PERF_MEASURE_ITERS = "24"
}

$env:CV_DETECTION_PERF_GATE_PROFILE = $effectiveGateProfile

if (-not [string]::IsNullOrWhiteSpace($ReportDirectory)) {
    $env:CV_DETECTION_PERF_REPORT_DIR = Resolve-RepoRelativePath $ReportDirectory
}
elseif (-not [string]::IsNullOrWhiteSpace($env:CV_DETECTION_PERF_REPORT_DIR)) {
    $env:CV_DETECTION_PERF_REPORT_DIR = Resolve-RepoRelativePath $env:CV_DETECTION_PERF_REPORT_DIR
}
elseif (-not [string]::IsNullOrWhiteSpace($env:CV_PERF_REPORT_DIR)) {
    $env:CV_PERF_REPORT_DIR = Resolve-RepoRelativePath $env:CV_PERF_REPORT_DIR
    $env:CV_DETECTION_PERF_REPORT_DIR = $env:CV_PERF_REPORT_DIR
}

if ($MinimumReportEntries -lt 1) {
    throw "MinimumReportEntries must be greater than or equal to 1."
}

if ([string]::IsNullOrWhiteSpace($env:CV_DETECTION_PERF_FAILURE_ARCHIVE_DIR)) {
    $env:CV_DETECTION_PERF_FAILURE_ARCHIVE_DIR = if (-not [string]::IsNullOrWhiteSpace($env:CV_DETECTION_PERF_REPORT_DIR)) {
        Join-Path $env:CV_DETECTION_PERF_REPORT_DIR "archive\detection_performance_failures"
    }
    else {
        Join-Path $repoRoot "ClearVision.Product\test_results\archive\detection_performance_failures"
    }
}
else {
    $env:CV_DETECTION_PERF_FAILURE_ARCHIVE_DIR = Resolve-RepoRelativePath $env:CV_DETECTION_PERF_FAILURE_ARCHIVE_DIR
}

Write-Host "[detection-perf] GateProfile=$effectiveGateProfile (reason: $($resolvedProfile.Reason))"
Write-Host "[detection-perf] CV_DETECTION_PERF_BUDGET_SCALE=$($env:CV_DETECTION_PERF_BUDGET_SCALE)"
Write-Host "[detection-perf] CV_DETECTION_PERF_WARMUP_ITERS=$($env:CV_DETECTION_PERF_WARMUP_ITERS)"
Write-Host "[detection-perf] CV_DETECTION_PERF_MEASURE_ITERS=$($env:CV_DETECTION_PERF_MEASURE_ITERS)"
Write-Host "[detection-perf] CV_DETECTION_PERF_REPORT_DIR=$($env:CV_DETECTION_PERF_REPORT_DIR)"
Write-Host "[detection-perf] CV_DETECTION_PERF_FAILURE_ARCHIVE_DIR=$($env:CV_DETECTION_PERF_FAILURE_ARCHIVE_DIR)"
Write-Host "[detection-perf] MinimumReportEntries=$MinimumReportEntries"

if ($ValidateReportOnly) {
    $exitCode = 0
    try {
        Test-PerformanceReportArtifacts `
            -ReportDirectory (Resolve-DetectionPerformanceReportDirectory) `
            -RunStartedAtUtc ([DateTime]::MinValue) `
            -MinimumEntries $MinimumReportEntries `
            -ExpectedEntries $expectedPerformanceEntries `
            -SkipFreshnessCheck
    }
    catch {
        Write-Host "[detection-perf] Report artifact validation failed: $($_.Exception.Message)"
        $exitCode = 1
    }

    $global:LASTEXITCODE = $exitCode
    if ($ReturnExitCode) {
        return
    }

    exit $exitCode
}

$parameters = @{
    Project = $project
    Filter = [string]$gateDefinition[0].filter
    Verbosity = $Verbosity
    ResultsDirectory = if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) { $defaultResultsDirectory } else { $ResultsDirectory }
    LogFileName = if ([string]::IsNullOrWhiteSpace($LogFileName)) { $defaultLogFileName } else { $LogFileName }
}

if ($MinimumTotalTests -gt 0) {
    $parameters.MinimumTotalTests = $MinimumTotalTests
}

if (-not [string]::IsNullOrWhiteSpace($Configuration)) {
    $parameters.Configuration = $Configuration
}

if ($NoBuild) {
    $parameters.NoBuild = $true
}

if ($NoRestore) {
    $parameters.NoRestore = $true
}

$parameters.ReturnExitCode = $true

$runStartedAtUtc = [DateTime]::UtcNow
& $runner @parameters
$exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }

if ($exitCode -eq 0) {
    try {
        Test-PerformanceReportArtifacts `
            -ReportDirectory (Resolve-DetectionPerformanceReportDirectory) `
            -RunStartedAtUtc $runStartedAtUtc `
            -MinimumEntries $MinimumReportEntries `
            -ExpectedEntries $expectedPerformanceEntries
    }
    catch {
        Write-Host "[detection-perf] Report artifact validation failed: $($_.Exception.Message)"
        $exitCode = 1
    }
}

$global:LASTEXITCODE = $exitCode

if ($ReturnExitCode) {
    return
}

exit $exitCode
