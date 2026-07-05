param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [ValidateSet("regression", "integration", "all")]
    [string]$Gate = "all",

    [string]$Configuration,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [string]$ResultsDirectory,

    [string]$LogFileName,

    [int]$MinimumTotalTests = -1,

    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$runner = Join-Path $scriptRoot "run-dotnet-test-serial.ps1"
$project = Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.Tests\ClearVision.Product.Tests.csproj"
$defaultResultsDirectory = Join-Path $repoRoot ".tmp\test_results\calibration-$Gate"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$defaultLogFileName = "calibration-$Gate-$timestamp.trx"
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
    "CalibrationLoaderOperatorTests",
    "CameraCalibrationOperatorTests",
    "CoordinateTransformOperatorTests",
    "FisheyeCalibrationOperatorTests",
    "FisheyeUndistortOperatorTests",
    "HandEyeCalibrationOperatorTests",
    "NPointCalibrationOperatorTests",
    "PixelToWorldTransformOperatorTests",
    "StereoCalibrationOperatorTests",
    "TranslationRotationCalibrationOperatorTests",
    "UndistortOperatorTests"
)

$integrationTestClasses = @(
    "Integration.CalibrationV2IntegrationTests",
    "Integration.LegacyCalibrationContractAuditTests"
)

$selectedTestClasses = switch ($Gate) {
    "regression" { $regressionTestClasses }
    "integration" { $integrationTestClasses }
    "all" { ($regressionTestClasses + $integrationTestClasses) | Select-Object -Unique }
    default { throw "Unsupported gate '$Gate'." }
}

Write-Host "[calibration-regression] Gate=$Gate"
Write-Host "[calibration-regression] Selected test classes: $($selectedTestClasses -join ', ')"

$defaultMinimumTotalTests = switch ($Gate) {
    "regression" { 105 }
    "integration" { 7 }
    "all" { 115 }
    default { 0 }
}

$effectiveMinimumTotalTests = if ($MinimumTotalTests -ge 0) { $MinimumTotalTests } else { $defaultMinimumTotalTests }

$parameters = @{
    Project = $project
    FullyQualifiedName = $selectedTestClasses
    Verbosity = $Verbosity
    ResultsDirectory = if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) { $defaultResultsDirectory } else { $ResultsDirectory }
    LogFileName = if ([string]::IsNullOrWhiteSpace($LogFileName)) { $defaultLogFileName } else { $LogFileName }
}

if ($effectiveMinimumTotalTests -gt 0) {
    $parameters.MinimumTotalTests = $effectiveMinimumTotalTests
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

& $runner @parameters

$exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
$global:LASTEXITCODE = $exitCode

if ($ReturnExitCode) {
    return
}

exit $exitCode
