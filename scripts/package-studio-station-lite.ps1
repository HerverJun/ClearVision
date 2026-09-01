[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$SourceRevisionId = "",
    [string]$OutputRoot = ".tmp\publish-check\wave3c\studio-station-diagnostic",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$canonical = Join-Path $PSScriptRoot "package-portable-deployment.ps1"
$shared = @{
    Configuration = $Configuration
    RuntimeIdentifier = "win-x64"
    Profile = "diagnostic-framework-dependent"
    Version = $Version
    SourceRevisionId = $SourceRevisionId
    NoRestore = $NoRestore
}

& $canonical @shared -Application Studio -OutputRoot (Join-Path $OutputRoot "Studio")
if ($LASTEXITCODE -ne 0) { throw "Canonical Studio diagnostic packaging failed." }

& $canonical @shared -Application Station -OutputRoot (Join-Path $OutputRoot "Station")
if ($LASTEXITCODE -ne 0) { throw "Canonical Station diagnostic packaging failed." }
