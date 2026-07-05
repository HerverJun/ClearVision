param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [string]$Configuration,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [int]$MinimumServiceTestClasses = 20,

    [int]$MinimumTotalTests = 200
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$runner = Join-Path $scriptRoot "run-dotnet-test-serial.ps1"
$project = Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.Tests\ClearVision.Product.Tests.csproj"
$testDirectory = Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.Tests"
$serviceTestFiles = @()
$serviceTestFiles += Get-ChildItem -LiteralPath (Join-Path $testDirectory "Services") -Filter "*Tests.cs" -File
$serviceTestFiles += Get-Item -LiteralPath (Join-Path $testDirectory "Events\InMemoryInspectionEventBusTests.cs")
$serviceTestFiles = $serviceTestFiles | Sort-Object FullName

if ($serviceTestFiles.Count -lt $MinimumServiceTestClasses) {
    throw "Expected at least $MinimumServiceTestClasses service regression test classes, but found $($serviceTestFiles.Count) in $testDirectory."
}

$serviceTestClasses = @()
foreach ($file in $serviceTestFiles) {
    $classDeclaration = Select-String -LiteralPath $file.FullName -Pattern '^\s*public\s+(?:sealed\s+|partial\s+)?class\s+([A-Za-z_][A-Za-z0-9_]*)' |
        Select-Object -First 1

    if ($null -eq $classDeclaration) {
        throw "Unable to find a public test class declaration in $($file.FullName)."
    }

    $serviceTestClasses += $classDeclaration.Matches[0].Groups[1].Value
}

$duplicateClasses = $serviceTestClasses |
    Group-Object |
    Where-Object { $_.Count -gt 1 } |
    Select-Object -ExpandProperty Name

if ($duplicateClasses.Count -gt 0) {
    throw "Duplicate service regression test class names discovered: $($duplicateClasses -join ', ')."
}

Write-Host "[services-regression] Discovered $($serviceTestClasses.Count) service regression test classes."

$parameters = @{
    Project = $project
    FullyQualifiedName = $serviceTestClasses
    Verbosity = $Verbosity
}

if ($MinimumTotalTests -gt 0) {
    $parameters.ResultsDirectory = Join-Path $repoRoot ".tmp\test_results\services-regression"
    $parameters.LogFileName = "services-regression.trx"
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

exit $exitCode
