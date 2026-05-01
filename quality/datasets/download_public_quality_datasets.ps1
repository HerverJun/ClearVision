param(
    [string[]]$Dataset = @("bsds500", "opencv_calibration_samples", "kolektorsdd2"),
    [string]$Proxy = "",
    [double]$BudgetGB = 20.0,
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

function Get-DirectorySizeBytes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return [int64]0
    }

    $sum = [int64]0
    Get-ChildItem -LiteralPath $Path -Recurse -File -Force | ForEach-Object {
        $sum += [int64]$_.Length
    }

    return $sum
}

function Assert-PublicDatasetBudget {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Destination,
        [long]$ExpectedSize = 0
    )

    if ($BudgetGB -le 0 -or $ExpectedSize -le 0) {
        return
    }

    $publicRoot = Resolve-RepoPath "quality/public_datasets"
    $budgetBytes = [int64]($BudgetGB * 1024 * 1024 * 1024)
    $usedBytes = Get-DirectorySizeBytes -Path $publicRoot
    $existingBytes = [int64]0
    if (Test-Path -LiteralPath $Destination) {
        $existingBytes = [int64](Get-Item -LiteralPath $Destination).Length
    }

    $projectedBytes = $usedBytes - $existingBytes + $ExpectedSize
    if ($projectedBytes -gt $budgetBytes) {
        $usedGb = [math]::Round($usedBytes / 1GB, 2)
        $projectedGb = [math]::Round($projectedBytes / 1GB, 2)
        throw "Refusing download because it would exceed the public dataset budget. Used=${usedGb}GB, projected=${projectedGb}GB, budget=${BudgetGB}GB. Increase -BudgetGB or download in smaller phases."
    }
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
    Assert-PublicDatasetBudget -Destination $Destination -ExpectedSize $ExpectedSize

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

function Assert-ArchiveHash {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Archive,
        [string]$ExpectedSha256 = ""
    )

    if (-not $ExpectedSha256) {
        return
    }

    $normalizedExpected = $ExpectedSha256.ToLowerInvariant().Replace("sha256:", "")
    $actual = (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $normalizedExpected) {
        throw "SHA256 mismatch for $Archive. Expected $normalizedExpected, got $actual."
    }
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

function Expand-TarXz {
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

    & tar.exe -xJf $Archive -C $Destination
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

function Download-ManifestDataset {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatasetId,
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath,
        [switch]$ManifestOnly
    )

    $resolvedManifest = Resolve-RepoPath $ManifestPath
    if (-not (Test-Path -LiteralPath $resolvedManifest)) {
        throw "Dataset manifest not found: $resolvedManifest"
    }

    $manifest = Get-Content -LiteralPath $resolvedManifest -Raw | ConvertFrom-Json
    $root = Resolve-RepoPath $manifest.local_root
    Assert-PathInsidePublicDatasets -Path $root
    New-Item -ItemType Directory -Force -Path $root | Out-Null

    Write-Host "Manifest: $resolvedManifest"
    Write-Host "Local root: $root"
    Write-Host "License: $($manifest.license.id)"
    Write-Host "Source: $($manifest.source.url)"

    if ($ManifestOnly) {
        Write-Host "Manifest-only registration complete; no archive download requested."
        return
    }

    if (-not $manifest.archives -or $manifest.archives.Count -eq 0) {
        Write-Warning "Manifest $ManifestPath has no scriptable archive. Registered manifest only; download manually under $root if the upstream host requires authentication or click-through terms."
        return
    }

    foreach ($archiveSpec in $manifest.archives) {
        if (-not $archiveSpec.source_url -or $archiveSpec.source_url -match "^<") {
            throw "Archive '$($archiveSpec.name)' in $ManifestPath has no concrete source_url."
        }

        $archiveName = if ($archiveSpec.name) { $archiveSpec.name } else { [System.IO.Path]::GetFileName($archiveSpec.source_url) }
        $archive = Join-Path $root "_downloads/$archiveName"
        $expectedSize = [int64]0
        if ($archiveSpec.PSObject.Properties.Name -contains "size_bytes" -and $null -ne $archiveSpec.size_bytes) {
            $expectedSize = [int64]$archiveSpec.size_bytes
        }

        Invoke-CurlDownload -Url $archiveSpec.source_url -Destination $archive -ExpectedSize $expectedSize
        $expectedHash = ""
        if ($archiveSpec.PSObject.Properties.Name -contains "sha256" -and $null -ne $archiveSpec.sha256) {
            $expectedHash = [string]$archiveSpec.sha256
        }

        Assert-ArchiveHash -Archive $archive -ExpectedSha256 $expectedHash

        $extractTo = if ($archiveSpec.extract_to) { $archiveSpec.extract_to } else { "extracted" }
        $destination = Join-Path $root $extractTo
        $archiveType = "$($archiveSpec.archive_type)".ToLowerInvariant()
        if ($archiveType -eq "tar.xz" -or $archiveName.EndsWith(".tar.xz", [System.StringComparison]::OrdinalIgnoreCase)) {
            Expand-TarXz -Archive $archive -Destination $destination
        }
        elseif ($archiveType -eq "tar.gz" -or $archiveName.EndsWith(".tgz", [System.StringComparison]::OrdinalIgnoreCase) -or $archiveName.EndsWith(".tar.gz", [System.StringComparison]::OrdinalIgnoreCase)) {
            Expand-TarGz -Archive $archive -Destination $destination
        }
        elseif ($archiveType -eq "zip" -or $archiveName.EndsWith(".zip", [System.StringComparison]::OrdinalIgnoreCase)) {
            Expand-Zip -Archive $archive -Destination $destination
        }
        else {
            Write-Warning "Downloaded $archiveName but did not extract it because archive_type is '$archiveType'."
        }
    }

    Write-Host "Dataset $DatasetId downloaded from manifest."
}

function Download-MvtecAdFull {
    Download-ManifestDataset -DatasetId "mvtec_ad_full" -ManifestPath "quality/datasets/mvtec_ad_full_manifest.json"
}

function Download-MvtecLocoAd {
    Download-ManifestDataset -DatasetId "mvtec_loco_ad" -ManifestPath "quality/datasets/mvtec_loco_ad_manifest.json"
}

function Download-BipedV2 {
    Download-ManifestDataset -DatasetId "biped_v2" -ManifestPath "quality/datasets/biped_v2_manifest.json"
}

function Download-Uded {
    Download-ManifestDataset -DatasetId "uded" -ManifestPath "quality/datasets/uded_manifest.json"
}

function Register-MvtecAd2PublicPart {
    Download-ManifestDataset -DatasetId "mvtec_ad2_public" -ManifestPath "quality/datasets/mvtec_ad2_public_manifest.json" -ManifestOnly
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
Write-Host "Public dataset root: $(Resolve-RepoPath "quality/public_datasets")"
Write-Host "Budget: $BudgetGB GB"
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
        "industrial_detection_priority" {
            Download-MvtecAdFull
            Download-MvtecLocoAd
            Download-BipedV2
            Download-Uded
            Register-MvtecAd2PublicPart
        }
        "bsds500" { Download-Bsds500 }
        "opencv_calibration_samples" { Download-OpenCvCalibrationSamples }
        "kolektorsdd2" { Download-KolektorSdd2 }
        "coco2017" { Download-Coco2017 }
        "hpatches" { Download-HPatches }
        "mvtec_ad_full" { Download-MvtecAdFull }
        "mvtec_loco_ad" { Download-MvtecLocoAd }
        "biped_v2" { Download-BipedV2 }
        "uded" { Download-Uded }
        "mvtec_ad2_public" { Register-MvtecAd2PublicPart }
        default { throw "Unknown dataset id: $datasetId" }
    }
}

Write-Host ""
Write-Host "Requested public quality datasets are ready."
