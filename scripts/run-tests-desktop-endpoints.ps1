param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [string]$Configuration,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [int]$MinimumEndpointTestClasses = 8,

    [int]$MinimumTotalTests = 200
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$runner = Join-Path $scriptRoot "run-dotnet-test-serial.ps1"
$project = Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.Desktop.Tests\ClearVision.Product.Desktop.Tests.csproj"
$testDirectory = Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.Desktop.Tests"

$endpointTestFiles = Get-ChildItem -LiteralPath $testDirectory -Filter "*Endpoint*Tests.cs" -File |
    Sort-Object Name

if ($endpointTestFiles.Count -lt $MinimumEndpointTestClasses) {
    throw "Expected at least $MinimumEndpointTestClasses Desktop endpoint test classes, but found $($endpointTestFiles.Count) in $testDirectory."
}

$endpointTestClasses = @()
foreach ($file in $endpointTestFiles) {
    $classDeclaration = Select-String -LiteralPath $file.FullName -Pattern '^\s*public\s+(?:sealed\s+|partial\s+)?class\s+([A-Za-z_][A-Za-z0-9_]*)' |
        Select-Object -First 1

    if ($null -eq $classDeclaration) {
        throw "Unable to find a public test class declaration in $($file.FullName)."
    }

    $endpointTestClasses += $classDeclaration.Matches[0].Groups[1].Value
}

$duplicateClasses = $endpointTestClasses |
    Group-Object |
    Where-Object { $_.Count -gt 1 } |
    Select-Object -ExpandProperty Name

if ($duplicateClasses.Count -gt 0) {
    throw "Duplicate Desktop endpoint test class names discovered: $($duplicateClasses -join ', ')."
}

Write-Host "[desktop-endpoints] Discovered $($endpointTestClasses.Count) endpoint test classes."

$parameters = @{
    Project = $project
    FullyQualifiedName = $endpointTestClasses
    Verbosity = $Verbosity
}

if ($MinimumTotalTests -gt 0) {
    $parameters.ResultsDirectory = Join-Path $repoRoot ".tmp\test_results\desktop-endpoints"
    $parameters.LogFileName = "desktop-endpoints.trx"
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
