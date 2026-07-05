param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [string]$Configuration,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [string]$ResultsDirectory,

    [string]$LogFileName,

    [int]$MinimumTotalTests = 120,

    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$runner = Join-Path $scriptRoot "run-dotnet-test-serial.ps1"
$project = Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.Tests\ClearVision.Product.Tests.csproj"
$defaultResultsDirectory = Join-Path $repoRoot "test_results"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$defaultLogFileName = "phase42-regression-$timestamp.trx"
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

$parameters = @{
    Project = $project
    FullyQualifiedName = @(
        "Phase42RegionProcessingOperatorTests",
        "Phase42MeasurementAndSignalOperatorTests",
        "LocalDeformableMatchingPhase42Tests",
        "PixelToWorldTransformOperatorTests",
        "PlanarMatchingOperatorTests",
        "ImageOutputLifecycleGuardTests",
        "OperatorMetadataMigrationTests",
        "OperatorContractReconciliationTests"
    )
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
