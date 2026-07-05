param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [string]$Configuration,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [string]$ResultsDirectory,

    [string]$LogFileName,

    [int]$MinimumTotalTests = 15,

    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$runner = Join-Path $scriptRoot "run-dotnet-test-serial.ps1"
$project = Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.Tests\ClearVision.Product.Tests.csproj"
$defaultResultsDirectory = Join-Path $repoRoot ".tmp\test_results\measurement-stability"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$defaultLogFileName = "measurement-stability-$timestamp.trx"

$testClasses = @(
    "MeasurementStabilityIntegrationTests"
)

Write-Host "[measurement-stability] Selected test classes: $($testClasses -join ', ')"

$parameters = @{
    Project = $project
    FullyQualifiedName = $testClasses
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

& $runner @parameters

$exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
$global:LASTEXITCODE = $exitCode

if ($ReturnExitCode) {
    return
}

exit $exitCode
