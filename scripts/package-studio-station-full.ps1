[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$SourceRevisionId = "",
    [string]$OutputRoot = ".tmp\publish-check\wave3c\studio-station-full",
    [switch]$NoRestore,
    [switch]$RunOperatorSmoke
)

$ErrorActionPreference = "Stop"
$canonical = Join-Path $PSScriptRoot "package-portable-deployment.ps1"
$shared = @{
    Configuration = $Configuration
    RuntimeIdentifier = "win-x64"
    Profile = "field-self-contained"
    Version = $Version
    SourceRevisionId = $SourceRevisionId
    NoRestore = $NoRestore
}

& $canonical @shared -Application Studio -OutputRoot (Join-Path $OutputRoot "Studio") -RunOperatorSmoke:$RunOperatorSmoke
if ($LASTEXITCODE -ne 0) { throw "Canonical Studio packaging failed." }

& $canonical @shared -Application Station -OutputRoot (Join-Path $OutputRoot "Station") -RunOperatorSmoke:$RunOperatorSmoke
if ($LASTEXITCODE -ne 0) { throw "Canonical Station packaging failed." }
