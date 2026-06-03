param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

$repoRoot = Resolve-RepoRoot
$publishDir = Join-Path $repoRoot "publish"
$tmpPublishRoot = Join-Path $repoRoot ".tmp\publish-check"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  ClearVision Studio & Station [LITE]" -ForegroundColor Cyan
Write-Host "  (Framework-Dependent Release Packaging)" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Clean temporary build directory
Write-Host "[1/5] Cleaning temporary staging..." -ForegroundColor Yellow
if (Test-Path $tmpPublishRoot) {
    Remove-Item -LiteralPath $tmpPublishRoot -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  [OK] Removed old build staging directory" -ForegroundColor Gray
}

Ensure-Directory $publishDir
Ensure-Directory $tmpPublishRoot

# 2. Check and prepare .NET Environment
Write-Host "[2/5] Preparing .NET environment..." -ForegroundColor Yellow
$dotnetShimPath = Join-Path $repoRoot "scripts\dotnet.ps1"
$dotnetPathOutput = & $dotnetShimPath -InstallIfMissing -PrintPath -ReturnExitCode
if ($LASTEXITCODE -ne 0) {
    throw "Failed to resolve .NET SDK via $dotnetShimPath."
}

$dotnetPath = ($dotnetPathOutput | Select-Object -Last 1).Trim()
if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
    throw "Resolved dotnet path is empty."
}
Write-Host "  Using .NET SDK path: $dotnetPath" -ForegroundColor Gray

# Generate standard timestamp
$timestamp = Get-Date -Format "yyyyMMdd-HHmm"
$studioZipName = "ClearVision-Studio-Lite-$timestamp.zip"
$stationZipName = "ClearVision-Station-Lite-$timestamp.zip"

# Clean target zip files if they already exist
$studioZipPath = Join-Path $publishDir $studioZipName
$stationZipPath = Join-Path $publishDir $stationZipName
if (Test-Path $studioZipPath) { Remove-Item -LiteralPath $studioZipPath -Force }
if (Test-Path $stationZipPath) { Remove-Item -LiteralPath $stationZipPath -Force }

$studioStaging = Join-Path $tmpPublishRoot "Studio"
$stationStaging = Join-Path $tmpPublishRoot "Station"

# 3. Compile and publish ClearVision Studio (LITE)
Write-Host "[3/5] Publishing ClearVision Studio [LITE]..." -ForegroundColor Yellow
$studioProject = Join-Path $repoRoot "ClearVision.Product\src\ClearVision.Product.Desktop\ClearVision.Product.Desktop.csproj"

& $dotnetPath publish $studioProject `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=false `
    -p:RestorePackagesWithLockFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $studioStaging

if ($LASTEXITCODE -ne 0) {
    throw "Studio publish failed with exit code $LASTEXITCODE"
}

# Archive Studio
$studioZipPath = Join-Path $publishDir $studioZipName
Write-Host "  Archiving Studio to $studioZipName..." -ForegroundColor Gray
Compress-Archive -Path "$studioStaging\*" -DestinationPath $studioZipPath -Force
Write-Host "  [OK] Studio LITE package successfully created: $studioZipPath" -ForegroundColor Green

# 4. Compile and publish ClearVision Station (LITE)
Write-Host "[4/5] Publishing ClearVision Station [LITE]..." -ForegroundColor Yellow
$stationProject = Join-Path $repoRoot "ClearVision.Product\src\ClearVision.Product.Station\ClearVision.Product.Station.csproj"

& $dotnetPath publish $stationProject `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=false `
    -p:RestorePackagesWithLockFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $stationStaging

if ($LASTEXITCODE -ne 0) {
    throw "Station publish failed with exit code $LASTEXITCODE"
}

# Archive Station
$stationZipPath = Join-Path $publishDir $stationZipName
Write-Host "  Archiving Station to $stationZipName..." -ForegroundColor Gray
Compress-Archive -Path "$stationStaging\*" -DestinationPath $stationZipPath -Force
Write-Host "  [OK] Station LITE package successfully created: $stationZipPath" -ForegroundColor Green

# 5. Clean staging directory
Write-Host "[5/5] Cleaning up temporary build staging..." -ForegroundColor Yellow
if (Test-Path $tmpPublishRoot) {
    Remove-Item -LiteralPath $tmpPublishRoot -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "  [OK] Cleaned up temporary staging" -ForegroundColor Gray

Write-Host ""
Write-Host "==========================================" -ForegroundColor Green
Write-Host " LITE Packaging process completed successfully!" -ForegroundColor Green
Write-Host "  Studio: $studioZipPath" -ForegroundColor White
Write-Host "  Station: $stationZipPath" -ForegroundColor White
Write-Host "==========================================" -ForegroundColor Green
