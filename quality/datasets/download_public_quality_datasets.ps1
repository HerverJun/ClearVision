param(
    [string[]]$Dataset = @("bsds500", "opencv_calibration_samples", "kolektorsdd2"),
    [string]$Proxy = "",
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

    return [System.IO.Path]::GetFullPath($root.Trim())
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

function Assert-PathInsideRepo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $repoRoot = [System.IO.Path]::GetFullPath($script:RepoRoot)
    if (-not $fullPath.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write outside repository: $fullPath"
    }
}

function Assert-PathInsidePublicDatasets {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $allowedRoot = [System.IO.Path]::GetFullPath((Join-Path $script:RepoRoot "quality/public_datasets"))
    $allowedPrefix = $allowedRoot.TrimEnd("\", "/") + [System.IO.Path]::DirectorySeparatorChar
    if ($fullPath -ne $allowedRoot -and -not $fullPath.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write outside quality/public_datasets: $fullPath"
    }
}

function Test-Command {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Get-ConfiguredProxy {
    if ($Proxy) {
        return $Proxy
    }

    foreach ($scope in @("Process", "User", "Machine")) {
        foreach ($name in @("HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY", "https_proxy", "http_proxy", "all_proxy")) {
            $value = [Environment]::GetEnvironmentVariable($name, $scope)
            if ($value) {
                return $value
            }
        }
    }

    foreach ($key in @("https.proxy", "http.proxy")) {
        $value = git config --global --get $key 2>$null
        if ($value) {
            return $value.Trim()
        }
    }

    return ""
}

function Invoke-CurlDownload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,
        [Parameter(Mandatory = $true)]
        [string]$Destination,
        [long]$ExpectedSize = 0
    )

    Assert-PathInsidePublicDatasets -Path $Destination

    $destinationDir = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null

    if ($Force -and (Test-Path -LiteralPath $Destination)) {
        Remove-Item -LiteralPath $Destination -Force
    }

    $curlArgs = @(
        "--ssl-no-revoke",
        "-L",
        "--fail",
        "--retry", "5",
        "--retry-delay", "2",
        "--retry-connrefused",
        "--retry-all-errors",
        "--connect-timeout", "30",
        "-C", "-",
        "--output", $Destination
    )

    if ($script:EffectiveProxy) {
        $curlArgs += @("--proxy", $script:EffectiveProxy)
    }

    Write-Host "Downloading $Url"
    Write-Host "  -> $Destination"
    if ($script:EffectiveProxy) {
        Write-Host "  proxy: $script:EffectiveProxy"
    }

    $curlArgs += $Url

    & curl.exe @curlArgs

    if ($LASTEXITCODE -ne 0) {
        throw "curl.exe failed with exit code $LASTEXITCODE for $Url"
    }

    if ($ExpectedSize -gt 0) {
        $actualSize = (Get-Item -LiteralPath $Destination).Length
        if ($actualSize -ne $ExpectedSize) {
            throw "Size mismatch for $Destination. Expected $ExpectedSize, got $actualSize."
        }
    }

    $hash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
    Write-Host "  sha256: $hash"
}

function Expand-TarGz {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Archive,
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    if ($SkipExtract) {
        return
    }

    Assert-PathInsidePublicDatasets -Path $Destination
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Write-Host "Extracting $Archive"
    Write-Host "  -> $Destination"

    & tar.exe -xzf $Archive -C $Destination
    if ($LASTEXITCODE -ne 0) {
        throw "tar.exe failed with exit code $LASTEXITCODE for $Archive"
    }
}

function Expand-Zip {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Archive,
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    if ($SkipExtract) {
        return
    }

    Assert-PathInsidePublicDatasets -Path $Destination
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Write-Host "Extracting $Archive"
    Write-Host "  -> $Destination"

    Expand-Archive -LiteralPath $Archive -DestinationPath $Destination -Force
}

function Download-Bsds500 {
    $root = Resolve-RepoPath "quality/public_datasets/bsds500"
    $archive = Join-Path $root "_downloads/BSR_bsds500.tgz"

    Invoke-CurlDownload `
        -Url "https://www2.eecs.berkeley.edu/Research/Projects/CS/vision/grouping/BSR/BSR_bsds500.tgz" `
        -Destination $archive `
        -ExpectedSize 70763455

    Expand-TarGz -Archive $archive -Destination (Join-Path $root "extracted")
}

function Download-OpenCvCalibrationSamples {
    $root = Resolve-RepoPath "quality/public_datasets/opencv_calibration_samples"
    New-Item -ItemType Directory -Force -Path $root | Out-Null

    $baseUrl = "https://raw.githubusercontent.com/opencv/opencv/4.x/samples/data"
    $indices = @(1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13, 14)
    $names = @("left.jpg", "right.jpg", "intrinsics.yml", "left_intrinsics.yml", "stereo_calib.xml")
    foreach ($i in $indices) {
        $names += "left{0:D2}.jpg" -f $i
        $names += "right{0:D2}.jpg" -f $i
    }

    foreach ($name in $names) {
        Invoke-CurlDownload -Url "$baseUrl/$name" -Destination (Join-Path $root $name)
    }
}

function Download-KolektorSdd2 {
    $root = Resolve-RepoPath "quality/public_datasets/kolektorsdd2"
    $archive = Join-Path $root "_downloads/KolektorSDD2.zip"

    Invoke-CurlDownload `
        -Url "https://go.vicos.si/kolektorsdd2" `
        -Destination $archive `
        -ExpectedSize 853126555

    Expand-Zip -Archive $archive -Destination (Join-Path $root "extracted")
}

function Download-Coco2017 {
    $root = Resolve-RepoPath "quality/public_datasets/coco2017"
    $valArchive = Join-Path $root "_downloads/val2017.zip"
    $annotationsArchive = Join-Path $root "_downloads/annotations_trainval2017.zip"

    Invoke-CurlDownload `
        -Url "http://images.cocodataset.org/zips/val2017.zip" `
        -Destination $valArchive

    Invoke-CurlDownload `
        -Url "http://images.cocodataset.org/annotations/annotations_trainval2017.zip" `
        -Destination $annotationsArchive

    Expand-Zip -Archive $valArchive -Destination (Join-Path $root "extracted")
    Expand-Zip -Archive $annotationsArchive -Destination (Join-Path $root "extracted")
}

function Download-HPatches {
    $root = Resolve-RepoPath "quality/public_datasets/hpatches"
    $archive = Join-Path $root "_downloads/hpatches-sequences-release.tar.gz"
    $fallbackArchive = Join-Path $root "_downloads/hpatches-sequences-release.zip"

    try {
        Invoke-CurlDownload `
            -Url "http://icvl.ee.ic.ac.uk/vbalnt/hpatches/hpatches-sequences-release.tar.gz" `
            -Destination $archive

        Expand-TarGz -Archive $archive -Destination (Join-Path $root "extracted")
    }
    catch {
        Write-Warning "Primary HPatches host failed: $($_.Exception.Message)"
        Write-Host "Falling back to the HPatches Hugging Face mirror published from the official dataset README."
        Invoke-CurlDownload `
            -Url "https://huggingface.co/datasets/vbalnt/hpatches/resolve/main/hpatches-sequences-release.zip" `
            -Destination $fallbackArchive

        Expand-Zip -Archive $fallbackArchive -Destination (Join-Path $root "extracted")
    }
}

if (-not (Test-Command "curl.exe")) {
    throw "curl.exe is required."
}

if (-not (Test-Command "tar.exe")) {
    throw "tar.exe is required for BSDS500 extraction."
}

$script:RepoRoot = Get-RepoRoot
$script:EffectiveProxy = Get-ConfiguredProxy

Write-Host "Repository: $script:RepoRoot"
if ($script:EffectiveProxy) {
    Write-Host "Using proxy: $script:EffectiveProxy"
}
else {
    Write-Host "Using direct network access."
}

foreach ($datasetId in $Dataset) {
    Write-Host ""
    Write-Host "Dataset: $datasetId"
    switch ($datasetId.ToLowerInvariant()) {
        "bsds500" { Download-Bsds500 }
        "opencv_calibration_samples" { Download-OpenCvCalibrationSamples }
        "kolektorsdd2" { Download-KolektorSdd2 }
        "coco2017" { Download-Coco2017 }
        "hpatches" { Download-HPatches }
        default { throw "Unknown dataset id: $datasetId" }
    }
}

Write-Host ""
Write-Host "Requested public quality datasets are ready."
