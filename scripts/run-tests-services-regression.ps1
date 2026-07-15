param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",
    [string]$Configuration,
    [switch]$NoBuild,
    [switch]$NoRestore,
    [int]$MinimumServiceTestClasses = 0,
    [int]$MinimumTotalTests = -1,
    [string]$ResultsDirectory,
    [string]$LogFileName,
    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"
if ($MinimumServiceTestClasses -gt 0) {
    Write-Warning "MinimumServiceTestClasses is retained for compatibility but no longer used; Suite=ServicesRegression is the fail-closed source."
}
$runner = Join-Path $PSScriptRoot "run-classified-test-gate.ps1"
$parameters = @{ Gate = "services-regression"; Verbosity = $Verbosity; ReturnExitCode = $true }
foreach ($name in @("Configuration", "ResultsDirectory", "LogFileName")) {
    $value = Get-Variable -Name $name -ValueOnly
    if (-not [string]::IsNullOrWhiteSpace($value)) { $parameters[$name] = $value }
}
if ($MinimumTotalTests -ge 0) { $parameters.MinimumTotalTests = $MinimumTotalTests }
if ($NoBuild) { $parameters.NoBuild = $true }
if ($NoRestore) { $parameters.NoRestore = $true }
& $runner @parameters
$exitCode = [int]$LASTEXITCODE
$global:LASTEXITCODE = $exitCode
if ($ReturnExitCode) { return }
exit $exitCode
