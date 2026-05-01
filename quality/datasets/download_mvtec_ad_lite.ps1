param(
    [string]$ManifestPath = "quality/datasets/mvtec_ad_lite_manifest.json",
    [switch]$Force,
    [switch]$SkipExtract
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Get-RepoRoot {
    $root = git rev-parse --show-toplevel 2>$null
    if (-not $root) {
        throw "Run this script from inside the ClearVision git repository."
    }
    return $root.Trim()
}

function Resolve-RepoPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return [System.IO.Path]::GetFullPath((Join-Path $script:RepoRoot $Path))
}

function Test-Command {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Assert-Tooling {
    if (-not (Test-Command "curl.exe")) {
        throw "curl.exe is required to download MVTec AD Lite archives."
    }

    if (-not (Test-Command "tar.exe")) {
        throw "tar.exe is required to extract .tar.xz archives."
    }
}

function Get-FileSize {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return 0
    }

    return (Get-Item -LiteralPath $Path).Length
}

function Test-ArchiveHash {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedSha256
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    return $actual.Equals($ExpectedSha256, [System.StringComparison]::OrdinalIgnoreCase)
}

function Download-Archive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    $destinationDir = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null

    Write-Host "Downloading $Url"
    Write-Host "  -> $Destination"

    & curl.exe `
        --ssl-no-revoke `
        -L `
        --fail `
        --retry 3 `
        --connect-timeout 30 `
        -C - `
        --output $Destination `
        $Url

    if ($LASTEXITCODE -ne 0) {
        throw "curl.exe failed with exit code $LASTEXITCODE for $Url"
    }
}

function Expand-ArchiveTarXz {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationRoot,
        [Parameter(Mandatory = $true)]
        [string]$ExtractedPath
    )

    if ((Test-Path -LiteralPath $ExtractedPath) -and -not $Force) {
        Write-Host "Extracted directory already exists: $ExtractedPath"
        return
    }

    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null

    Write-Host "Extracting $ArchivePath"
    Write-Host "  -> $DestinationRoot"

    & tar.exe -xf $ArchivePath -C $DestinationRoot

    if ($LASTEXITCODE -ne 0) {
        throw "tar.exe failed with exit code $LASTEXITCODE for $ArchivePath"
    }
}

$script:RepoRoot = Get-RepoRoot
Assert-Tooling

$manifestFullPath = Resolve-RepoPath $ManifestPath
if (-not (Test-Path -LiteralPath $manifestFullPath)) {
    throw "Manifest not found: $manifestFullPath"
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
$localRoot = Resolve-RepoPath $manifest.local_root
New-Item -ItemType Directory -Force -Path $localRoot | Out-Null

Write-Host "Dataset: $($manifest.name)"
Write-Host "Local root: $localRoot"
Write-Host "License: $($manifest.license.spdx_like)"

foreach ($category in $manifest.categories) {
    $archive = $category.archive
    $archivePath = Resolve-RepoPath $archive.path
    $extractedPath = Resolve-RepoPath $category.extracted_path

    Write-Host ""
    Write-Host "Category: $($category.name) [$($category.kind)]"

    if ($Force -and (Test-Path -LiteralPath $archivePath)) {
        Write-Host "Force enabled; removing existing archive: $archivePath"
        Remove-Item -LiteralPath $archivePath -Force
    }

    $archiveValid = Test-ArchiveHash -Path $archivePath -ExpectedSha256 $archive.sha256
    if ($archiveValid) {
        Write-Host "Archive hash OK: $archivePath"
    }
    else {
        $currentSize = Get-FileSize -Path $archivePath
        if ($currentSize -gt 0) {
            Write-Host "Archive missing or incomplete; current size is $currentSize bytes."
        }
        Download-Archive -Url $archive.source_url -Destination $archivePath

        $actualSize = Get-FileSize -Path $archivePath
        if ($actualSize -ne [int64]$archive.size_bytes) {
            throw "Archive size mismatch for $($category.name): expected $($archive.size_bytes), got $actualSize"
        }

        if (-not (Test-ArchiveHash -Path $archivePath -ExpectedSha256 $archive.sha256)) {
            $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
            throw "Archive SHA256 mismatch for $($category.name): expected $($archive.sha256), got $actualHash"
        }

        Write-Host "Archive hash OK: $archivePath"
    }

    if (-not $SkipExtract) {
        Expand-ArchiveTarXz -ArchivePath $archivePath -DestinationRoot $localRoot -ExtractedPath $extractedPath
    }
}

Write-Host ""
Write-Host "MVTec AD Lite is ready at: $localRoot"
