param(
    [string]$Studio = "http://127.0.0.1:5000",
    [string]$Token = "",
    [int]$Stations = 8,
    [double]$Rate = 1,
    [double]$NgRate = 0.08,
    [double]$ErrorRate = 0.01,
    [double]$LogRate = 0.02,
    [double]$DisconnectRate = 0,
    [int]$DurationSeconds = 600
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$dotnetShimPath = Join-Path $scriptRoot "dotnet.ps1"
$dotnetPathOutput = & $dotnetShimPath -InstallIfMissing -PrintPath -ReturnExitCode
if ($LASTEXITCODE -ne 0) {
    throw "Unable to resolve repository .NET SDK with $dotnetShimPath."
}

$dotnetPath = ($dotnetPathOutput | Select-Object -Last 1).Trim()
if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
    throw "Resolved dotnet path is empty."
}

& $dotnetPath run --project (Join-Path $repoRoot "Acme.Product/src/Acme.Product.Station.Simulator/Acme.Product.Station.Simulator.csproj") -- `
    --studio $Studio `
    --token $Token `
    --stations $Stations `
    --rate $Rate `
    --ng-rate $NgRate `
    --error-rate $ErrorRate `
    --log-rate $LogRate `
    --disconnect-rate $DisconnectRate `
    --duration $DurationSeconds
