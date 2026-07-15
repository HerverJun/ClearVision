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
$runner = Join-Path $PSScriptRoot "run-classified-test-gate.ps1"
$gateNames = switch ($Gate) {
    "regression" { @("calibration-regression") }
    "integration" { @("calibration-integration") }
    "all" { @("calibration-regression", "calibration-integration") }
}

$exitCode = 0
foreach ($gateName in $gateNames) {
    $parameters = @{ Gate = $gateName; Verbosity = $Verbosity; ReturnExitCode = $true }
    if (-not [string]::IsNullOrWhiteSpace($Configuration)) { $parameters.Configuration = $Configuration }
    if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) { $parameters.ResultsDirectory = $ResultsDirectory }
    if (-not [string]::IsNullOrWhiteSpace($LogFileName)) {
        $parameters.LogFileName = if ($gateNames.Count -eq 1) {
            $LogFileName
        }
        else {
            "{0}-{1}{2}" -f [IO.Path]::GetFileNameWithoutExtension($LogFileName), $gateName, [IO.Path]::GetExtension($LogFileName)
        }
    }
    if ($MinimumTotalTests -ge 0 -and $gateNames.Count -eq 1) { $parameters.MinimumTotalTests = $MinimumTotalTests }
    if ($NoBuild) { $parameters.NoBuild = $true }
    if ($NoRestore) { $parameters.NoRestore = $true }

    & $runner @parameters
    if ($LASTEXITCODE -ne 0) {
        $exitCode = [int]$LASTEXITCODE
        break
    }
}

$global:LASTEXITCODE = $exitCode
if ($ReturnExitCode) { return }
exit $exitCode
