param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [ValidateSet("regression", "detection-accuracy", "detection-stability", "all")]
    [string]$Gate = "regression",

    [string]$Configuration,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [string]$ResultsDirectory,

    [string]$LogFileName,

    [int]$MinimumTotalTests = 0,

    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$runner = Join-Path $scriptRoot "run-dotnet-test-serial.ps1"
$project = Join-Path $repoRoot "Acme.Product\tests\Acme.Product.Tests\Acme.Product.Tests.csproj"
$defaultResultsDirectory = Join-Path $repoRoot "test_results"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$defaultLogFileName = "detection-$Gate-$timestamp.trx"
$repoDotnetHome = Join-Path $repoRoot ".dotnet-home"
$repoNuGetPackages = Join-Path $repoRoot ".dotnet\.nuget\packages"

if ([string]::IsNullOrWhiteSpace($env:DOTNET_CLI_HOME) -and (Test-Path $repoDotnetHome)) {
    $env:DOTNET_CLI_HOME = $repoDotnetHome
}

if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES) -and (Test-Path $repoNuGetPackages)) {
    $env:NUGET_PACKAGES = $repoNuGetPackages
}

if ([string]::IsNullOrWhiteSpace($env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE)) {
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
}

if ([string]::IsNullOrWhiteSpace($env:DOTNET_NOLOGO)) {
    $env:DOTNET_NOLOGO = "1"
}

$regressionTestClasses = @(
    "AngleMeasurementOperatorTests",
    "CaliperToolOperatorTests",
    "CircleMeasurementOperatorTests",
    "ContourMeasurementOperatorTests",
    "GapMeasurementOperatorTests",
    "GeoMeasurementOperatorTests",
    "GeometricToleranceOperatorTests",
    "HistogramAnalysisOperatorTests",
    "LineLineDistanceOperatorTests",
    "LineMeasurementOperatorTests",
    "MeasureDistanceOperatorTests",
    "PixelStatisticsOperatorTests",
    "PointLineDistanceOperatorTests",
    "SharpnessEvaluationOperatorTests",
    "WidthMeasurementOperatorTests",
    "OperatorContractReconciliationTests"
)

$accuracyTestClasses = @(
    "DetectionSequenceJudgeOperatorTests",
    "EdgePairDefectOperatorTests",
    "SurfaceDefectDetectionOperatorTests",
    "DeepLearningOperatorTests",
    "ColorDetectionOperatorTests",
    "BlobDetectionOperatorTests",
    "AnomalyDetectionOperatorTests",
    "MatchingIndustrialAcceptanceTests",
    "WireSequenceScenarioPackageTests"
)

$stabilityTestClasses = @(
    "Acme.Product.Tests.Operators.MatchingRegressionStabilityTests",
    "Integration.PerformanceAcceptanceTests",
    "OperatorContractReconciliationTests"
)

$selectedTestClasses = switch ($Gate) {
    "regression" { $regressionTestClasses }
    "detection-accuracy" { $accuracyTestClasses }
    "detection-stability" { $stabilityTestClasses }
    "all" { ($regressionTestClasses + $accuracyTestClasses + $stabilityTestClasses) | Select-Object -Unique }
    default { throw "Unsupported gate '$Gate'." }
}

Write-Host "[detection-regression] Gate=$Gate"
Write-Host "[detection-regression] Selected test classes: $($selectedTestClasses -join ', ')"

$parameters = @{
    Project = $project
    FullyQualifiedName = $selectedTestClasses
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

if ($ReturnExitCode) {
    $parameters.ReturnExitCode = $true
}

& $runner @parameters

if ($ReturnExitCode) {
    return
}
