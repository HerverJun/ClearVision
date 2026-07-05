param(
    [string]$Configuration = "Debug",

    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [switch]$NoBuild,

    [switch]$NoRestore,

    [ValidateSet("run", "validate-only", "dry-run", "list")]
    [string]$SuiteMode = "run",

    [string]$ResultsDirectory = ".tmp/test_results/operator-contract-smoke",

    [string]$LogFileName = "operator-contract-smoke.trx",

    [int]$MinimumTotalTests = 80,

    [switch]$XunitSmokeOnly
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
$runner = Join-Path $scriptDir "run-dotnet-test-serial.ps1"
$testProject = Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.Tests\ClearVision.Product.Tests.csproj"
$qualitySuiteRunner = Join-Path $repoRoot "quality\tools\run_quality_suite.py"

if (-not (Test-Path $testProject)) {
    throw "Test project not found: $testProject"
}

if (-not (Test-Path $qualitySuiteRunner)) {
    throw "Quality suite runner not found: $qualitySuiteRunner"
}

$testClasses = @(
    "OperatorContractReconciliationTests",
    "Sprint3_TypeConvertTests",
    "TriggerModuleOperatorTests",
    "CircleMeasurementOperatorTests",
    "ResultOutputOperatorTests",
    "ShapeMatchingOperatorTests",
    "WidthMeasurementOperatorTests"
)

if (-not $XunitSmokeOnly) {
    $suiteArguments = @(
        $qualitySuiteRunner
        "--suite"
        "quick_contract_suite"
    )

    switch ($SuiteMode) {
        "run" { $suiteArguments += "--run" }
        "validate-only" { $suiteArguments += "--validate-only" }
        "dry-run" { $suiteArguments += "--dry-run" }
        "list" { $suiteArguments += "--list" }
    }

    Write-Host "Running quick contract quality suite ($SuiteMode)..." -ForegroundColor Cyan
    & python @suiteArguments
    exit $LASTEXITCODE
}

$parameters = @{
    Project = $testProject
    FullyQualifiedName = $testClasses
    Configuration = $Configuration
    Verbosity = $Verbosity
    ResultsDirectory = $ResultsDirectory
    LogFileName = $LogFileName
    MinimumTotalTests = $MinimumTotalTests
    ReturnExitCode = $true
}

if ($NoBuild) {
    $parameters.NoBuild = $true
}

if ($NoRestore) {
    $parameters.NoRestore = $true
}

Write-Host "Running xUnit operator contract smoke suite through serial test runner..." -ForegroundColor Cyan
& $runner @parameters
$exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
exit $exitCode
