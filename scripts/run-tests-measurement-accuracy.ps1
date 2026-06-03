param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

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
$project = Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.Tests\ClearVision.Product.Tests.csproj"

$testClasses = @(
    "AngleMeasurementOperatorTests",
    "CaliperToolOperatorTests",
    "LineMeasurementOperatorTests",
    "LineLineDistanceOperatorTests",
    "GeoMeasurementOperatorTests",
    "MeasureDistanceOperatorTests",
    "PointLineDistanceOperatorTests",
    "WidthMeasurementOperatorTests",
    "GapMeasurementOperatorTests",
    "CircleMeasurementOperatorTests",
    "ContourMeasurementOperatorTests",
    "ColorMeasurementOperatorTests",
    "HistogramAnalysisOperatorTests",
    "GeometricFittingOperatorTests",
    "GeometricToleranceOperatorTests",
    "SharpnessEvaluationOperatorTests",
    "PixelStatisticsOperatorTests"
)

Write-Host "[measurement-accuracy] Selected test classes: $($testClasses -join ', ')"

$parameters = @{
    Project = $project
    FullyQualifiedName = $testClasses
    Verbosity = $Verbosity
}

if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $parameters.ResultsDirectory = $ResultsDirectory
}

if (-not [string]::IsNullOrWhiteSpace($LogFileName)) {
    $parameters.LogFileName = $LogFileName
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
