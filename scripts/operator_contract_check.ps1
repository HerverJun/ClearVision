param(
    [string]$Configuration = "Debug",

    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [switch]$NoBuild,

    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
$runner = Join-Path $scriptDir "run-dotnet-test-serial.ps1"
$testProject = Join-Path $repoRoot "Acme.Product\tests\Acme.Product.Tests\Acme.Product.Tests.csproj"

if (-not (Test-Path $testProject)) {
    throw "Test project not found: $testProject"
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

$parameters = @{
    Project = $testProject
    FullyQualifiedName = $testClasses
    Configuration = $Configuration
    Verbosity = $Verbosity
}

if ($NoBuild) {
    $parameters.NoBuild = $true
}

if ($NoRestore) {
    $parameters.NoRestore = $true
}

Write-Host "Running operator contract regression suite through serial test runner..." -ForegroundColor Cyan
& $runner @parameters
