param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",
    [ValidateSet("phase1", "phase2")]
    [string]$Gate = "phase2",
    [string]$Configuration,
    [switch]$NoBuild,
    [switch]$NoRestore,
    [string]$ResultsDirectory,
    [string]$LogFileName,
    [int]$MinimumTotalTests = -1,
    [switch]$SkipDockerIntegration,
    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$runner = Join-Path $scriptRoot "run-classified-test-gate.ps1"
$gateName = if ($Gate -eq "phase1") { "data-processing-phase1" } else { "data-processing-phase2" }

function Test-DockerAccessible {
    try {
        $process = Start-Process -FilePath "docker" -ArgumentList @("info", "--format", "{{.ServerVersion}}") -NoNewWindow -PassThru -Wait -RedirectStandardOutput "$env:TEMP\cv_docker_info.out" -RedirectStandardError "$env:TEMP\cv_docker_info.err"
        return $process.ExitCode -eq 0
    }
    catch {
        return $false
    }
}

function Test-DockerImageAvailable {
    param([Parameter(Mandatory = $true)][string]$Image)
    $process = Start-Process -FilePath "docker" -ArgumentList @("image", "inspect", $Image) -NoNewWindow -PassThru -Wait -RedirectStandardOutput "$env:TEMP\cv_docker_image.out" -RedirectStandardError "$env:TEMP\cv_docker_image.err"
    return $process.ExitCode -eq 0
}

function Ensure-DockerImageAvailable {
    param([Parameter(Mandatory = $true)][string]$Image)
    if (Test-DockerImageAvailable -Image $Image) { return }
    Write-Host "[data-processing-remediation] Pulling missing image: $Image"
    $process = Start-Process -FilePath "docker" -ArgumentList @("pull", $Image) -NoNewWindow -PassThru -Wait -RedirectStandardOutput "$env:TEMP\cv_docker_pull.out" -RedirectStandardError "$env:TEMP\cv_docker_pull.err"
    if ($process.ExitCode -ne 0) {
        throw "Docker image '$Image' is unavailable and the pull failed."
    }
}

if (-not $SkipDockerIntegration) {
    if (-not (Test-DockerAccessible)) {
        throw "Docker is required for the declared Database resource gate. Start Docker or pass -SkipDockerIntegration for the PR-safe subset."
    }
    Ensure-DockerImageAvailable -Image "mcr.microsoft.com/mssql/server:2022-latest"
    Ensure-DockerImageAvailable -Image "mariadb:11.4"
}

$effectiveResultsDirectory = if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    Join-Path $repoRoot ".tmp\test_results\data-processing-remediation-$Gate"
}
else {
    $ResultsDirectory
}
$gateNames = @($gateName)
if (-not $SkipDockerIntegration) { $gateNames += "data-processing-docker" }
$exitCode = 0

foreach ($currentGate in $gateNames) {
    $parameters = @{
        Gate = $currentGate
        Verbosity = $Verbosity
        ResultsDirectory = $effectiveResultsDirectory
        LogFileName = if ([string]::IsNullOrWhiteSpace($LogFileName)) { "$currentGate.trx" } elseif ($gateNames.Count -eq 1) { $LogFileName } else { "{0}-{1}{2}" -f [IO.Path]::GetFileNameWithoutExtension($LogFileName), $currentGate, [IO.Path]::GetExtension($LogFileName) }
        ReturnExitCode = $true
    }
    if ($MinimumTotalTests -ge 0 -and $currentGate -eq $gateName) { $parameters.MinimumTotalTests = $MinimumTotalTests }
    if (-not [string]::IsNullOrWhiteSpace($Configuration)) { $parameters.Configuration = $Configuration }
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
