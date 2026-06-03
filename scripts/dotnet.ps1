param(
    [switch]$InstallIfMissing,

    [switch]$PrintPath,

    [switch]$ReturnExitCode,

    [Parameter(ValueFromRemainingArguments = $true, Position = 0)]
    [string[]]$Arguments
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$globalJsonPath = Join-Path $repoRoot "global.json"

if (-not (Test-Path -LiteralPath $globalJsonPath)) {
    throw "Cannot find repository global.json: $globalJsonPath"
}

$globalJson = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json
$requiredSdk = [string]$globalJson.sdk.version
$requiredRuntimeVersion = "8.0.26"
$requiredRuntimeBand = "8.0"
$requiredRuntimes = @(
    "Microsoft.NETCore.App",
    "Microsoft.AspNetCore.App",
    "Microsoft.WindowsDesktop.App"
)

if ([string]::IsNullOrWhiteSpace($requiredSdk)) {
    throw "global.json does not contain sdk.version."
}

function Add-Candidate {
    param(
        [System.Collections.Generic.List[string]]$Candidates,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $expanded = [Environment]::ExpandEnvironmentVariables($Path)
    if (-not (Test-Path -LiteralPath $expanded -PathType Leaf)) {
        return
    }

    $fullPath = [System.IO.Path]::GetFullPath($expanded)
    if (-not $Candidates.Contains($fullPath)) {
        [void]$Candidates.Add($fullPath)
    }
}

function Get-DotnetCandidates {
    $candidates = [System.Collections.Generic.List[string]]::new()

    Add-Candidate $candidates $env:CLEARVISION_DOTNET
    Add-Candidate $candidates $env:DOTNET_EXE

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        Add-Candidate $candidates (Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe")
    }

    Add-Candidate $candidates (Join-Path $repoRoot ".dotnet\dotnet.exe")
    Add-Candidate $candidates "$env:ProgramFiles\dotnet\dotnet.exe"
    Add-Candidate $candidates "${env:ProgramFiles(x86)}\dotnet\dotnet.exe"

    foreach ($command in (Get-Command dotnet -All -ErrorAction SilentlyContinue)) {
        Add-Candidate $candidates $command.Source
    }

    return $candidates
}

function Test-DotnetHasRequiredSdk {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DotnetPath
    )

    Push-Location -LiteralPath $repoRoot
    try {
        $null = & $DotnetPath --version 2>$null
        return $LASTEXITCODE -eq 0
    }
    finally {
        Pop-Location
    }
}

function Get-DotnetInstallScript {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is not set; cannot choose a per-user dotnet install directory."
    }

    $tmpDir = Join-Path $repoRoot ".tmp"
    $installerPath = Join-Path $tmpDir "dotnet-install.ps1"

    New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null

    if (-not (Test-Path -LiteralPath $installerPath)) {
        Invoke-WebRequest `
            -Uri "https://dot.net/v1/dotnet-install.ps1" `
            -OutFile $installerPath `
            -UseBasicParsing
    }

    Unblock-File -LiteralPath $installerPath -ErrorAction SilentlyContinue
    return $installerPath
}

function Invoke-DotnetInstall {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version,

        [string]$Runtime,

        [Parameter(Mandatory = $true)]
        [string]$InstallDir,

        [string]$Architecture = "x64"
    )

    $installerPath = Get-DotnetInstallScript
    $installParameters = @{
        Version = $Version
        InstallDir = $InstallDir
        Architecture = $Architecture
        NoPath = $true
    }

    if (-not [string]::IsNullOrWhiteSpace($Runtime)) {
        $installParameters.Runtime = $Runtime
    }

    & $installerPath @installParameters

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-install failed with exit code $LASTEXITCODE."
    }
}

function Install-RequiredSdk {
    if (-not $InstallIfMissing) {
        $candidateList = (Get-DotnetCandidates) -join [Environment]::NewLine
        throw @"
A .NET SDK compatible with global.json baseline $requiredSdk was not found by any usable dotnet host.

Candidates checked:
$candidateList

Run this once to install/use the pinned SDK:
  & ".\scripts\dotnet.ps1" -InstallIfMissing --version
"@
    }

    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is not set; cannot choose a per-user dotnet install directory."
    }

    $installDir = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null

    Write-Host "[dotnet] Installing .NET SDK $requiredSdk into $installDir"
    Invoke-DotnetInstall -Version $requiredSdk -InstallDir $installDir


    return (Join-Path $installDir "dotnet.exe")
}

function Test-DotnetHasRuntime {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DotnetPath,

        [Parameter(Mandatory = $true)]
        [string]$RuntimeName
    )

    $runtimeLines = & $DotnetPath --list-runtimes 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    foreach ($line in $runtimeLines) {
        if ($line -match "^\s*$([regex]::Escape($RuntimeName))\s+$([regex]::Escape($requiredRuntimeBand))\.") {
            return $true
        }
    }

    return $false
}

function Install-RequiredRuntimes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DotnetPath
    )

    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is not set; cannot choose a per-user dotnet install directory."
    }

    $installDir = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null

    $runtimeInstallKinds = @{
        "Microsoft.NETCore.App" = "dotnet"
        "Microsoft.AspNetCore.App" = "aspnetcore"
        "Microsoft.WindowsDesktop.App" = "windowsdesktop"
    }

    $missingRuntimes = @()
    foreach ($runtimeName in $requiredRuntimes) {
        if (-not (Test-DotnetHasRuntime -DotnetPath $DotnetPath -RuntimeName $runtimeName)) {
            $missingRuntimes += $runtimeName
        }
    }

    if ($missingRuntimes.Count -eq 0) {
        return
    }

    & $DotnetPath build-server shutdown 2>$null | Out-Null

    foreach ($runtimeName in $missingRuntimes) {
        $runtimeKind = $runtimeInstallKinds[$runtimeName]
        Write-Host "[dotnet] Installing $runtimeName $requiredRuntimeVersion into $installDir"
        Invoke-DotnetInstall -Runtime $runtimeKind -Version $requiredRuntimeVersion -InstallDir $installDir
    }
}

$dotnetPath = $null
foreach ($candidate in (Get-DotnetCandidates)) {
    if (Test-DotnetHasRequiredSdk -DotnetPath $candidate) {
        $dotnetPath = $candidate
        break
    }
}

if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
    $dotnetPath = Install-RequiredSdk
    if (-not (Test-DotnetHasRequiredSdk -DotnetPath $dotnetPath)) {
        throw "Installed dotnet host cannot resolve a compatible SDK for ${requiredSdk}: $dotnetPath"
    }
}

if ($InstallIfMissing) {
    Install-RequiredRuntimes -DotnetPath $dotnetPath
}

if ([string]::IsNullOrWhiteSpace($env:DOTNET_CLI_HOME)) {
    $env:DOTNET_CLI_HOME = Join-Path $repoRoot ".dotnet_cli_home"
}

New-Item -ItemType Directory -Path $env:DOTNET_CLI_HOME -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE)) {
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
}

if ([string]::IsNullOrWhiteSpace($env:DOTNET_NOLOGO)) {
    $env:DOTNET_NOLOGO = "1"
}

if ($PrintPath) {
    Write-Output $dotnetPath
    $global:LASTEXITCODE = 0
    if ($ReturnExitCode) {
        return
    }

    exit 0
}

if ($Arguments.Count -eq 0) {
    Write-Host "dotnet: $dotnetPath"
    & $dotnetPath --version
    $exitCode = $LASTEXITCODE
}
else {
    & $dotnetPath @Arguments
    $exitCode = $LASTEXITCODE
}

$global:LASTEXITCODE = $exitCode

if ($ReturnExitCode) {
    return
}

exit $exitCode
