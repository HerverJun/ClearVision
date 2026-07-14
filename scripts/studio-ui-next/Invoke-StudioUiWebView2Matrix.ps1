[CmdletBinding()]
param(
    [string]$NodeExecutablePath,
    [string]$DebugDesktopExecutablePath,
    [string]$RunName,
    [string]$EvidenceDirectory,
    [string]$RuntimeDirectory,
    [string]$PublishDirectory,
    [int]$BaseWebPort = 5300,
    [int]$BaseCdpPort = 9623,
    [int]$PerformanceGroups = 1,
    [switch]$SkipDebugBuild,
    [switch]$SkipPublish,
    [switch]$SkipPerformance,
    [switch]$KeepTemporaryPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot "../.."))
$singleRun = Join-Path $scriptRoot "Invoke-StudioUiWebView2Evidence.ps1"
$performanceRun = Join-Path $scriptRoot "Invoke-StudioUiCanvasPerformanceEvidence.ps1"
$dpiAudit = Join-Path $scriptRoot "Test-StudioUiDpiEvidence.ps1"
$noNodeAudit = Join-Path $scriptRoot "Test-StudioUiNoNodeEvidence.ps1"
$dotnetRunner = Join-Path $repoRoot "scripts/dotnet.ps1"
$desktopProject = Join-Path $repoRoot (
    "ClearVision.Product/src/ClearVision.Product.Desktop/" +
    "ClearVision.Product.Desktop.csproj")
$nodeExe = if ([string]::IsNullOrWhiteSpace($NodeExecutablePath)) {
    (Get-Command node.exe -ErrorAction Stop).Source
} else {
    [System.IO.Path]::GetFullPath($NodeExecutablePath)
}
$debugExe = if ([string]::IsNullOrWhiteSpace($DebugDesktopExecutablePath)) {
    Join-Path $repoRoot (
        "ClearVision.Product/src/ClearVision.Product.Desktop/bin/Debug/" +
        "net8.0-windows/win-x64/ClearVision.Product.Desktop.exe")
} else {
    [System.IO.Path]::GetFullPath($DebugDesktopExecutablePath)
}

if ([string]::IsNullOrWhiteSpace($RunName)) {
    $RunName = "webview2-matrix-{0}" -f [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss-fff")
}
$RunName = ($RunName -replace '[^A-Za-z0-9_.-]+', '-').Trim('-')
if ([string]::IsNullOrWhiteSpace($RunName)) {
    throw "RunName must contain at least one safe filename character."
}

$relativeEvidenceRoot = if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    ".tmp/studio-ui-next/f01/matrix/$RunName"
} else {
    $EvidenceDirectory.Replace('\', '/')
}
if ([System.IO.Path]::IsPathRooted($relativeEvidenceRoot)) {
    throw "EvidenceDirectory must be repository-relative."
}
$evidenceRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $relativeEvidenceRoot))
$allowedEvidenceRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ".tmp/studio-ui-next"))
$allowedEvidencePrefix = $allowedEvidenceRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $evidenceRoot.StartsWith(
    $allowedEvidencePrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Matrix evidence must remain under .tmp/studio-ui-next."
}
if (Test-Path -LiteralPath $evidenceRoot) {
    throw "Matrix evidence root already exists; use a unique RunName: $evidenceRoot"
}

$runtimeDirectoryRoot = if ([string]::IsNullOrWhiteSpace($RuntimeDirectory)) {
    Join-Path $repoRoot ".tmp/studio-ui-next/f01/runtime/$RunName"
} else {
    [System.IO.Path]::GetFullPath($RuntimeDirectory)
}
$runtimeCleanupParent = [System.IO.Path]::GetFullPath((Split-Path -Parent $runtimeDirectoryRoot))
$runtimeVolumeRoot = [System.IO.Path]::GetPathRoot($runtimeCleanupParent)
if ([string]::Equals(
    $runtimeCleanupParent.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar),
    $runtimeVolumeRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar),
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "RuntimeDirectory must be nested below a dedicated temporary parent."
}
if (Test-Path -LiteralPath $runtimeDirectoryRoot) {
    throw "RuntimeDirectory already exists; use an isolated path: $runtimeDirectoryRoot"
}
$runtimePrefix = $runtimeCleanupParent.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $runtimeDirectoryRoot.StartsWith(
    $runtimePrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "RuntimeDirectory must be a child of its dedicated temporary parent."
}

$publishRoot = if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    Join-Path $repoRoot ".tmp/publish-check/studio-ui-next-f01/$RunName/publish"
} else {
    [System.IO.Path]::GetFullPath($PublishDirectory)
}
$publishCheckRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $publishRoot))
$publishVolumeRoot = [System.IO.Path]::GetPathRoot($publishCheckRoot)
if ([string]::Equals(
    $publishCheckRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar),
    $publishVolumeRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar),
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "PublishDirectory must be nested below a dedicated temporary parent."
}
$missingAssetsRoot = Join-Path (Split-Path -Parent $publishRoot) "missing-assets-publish"
$publishArtifactsRoot = Join-Path (Split-Path -Parent $publishRoot) "artifacts"
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null

function Assert-TemporaryPath {
    param([string]$Path, [string]$AllowedRoot)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetFullPath($AllowedRoot)
    $prefix = $root.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe temporary path '$resolved'; expected a child of '$root'."
    }
    return $resolved
}

function Remove-VerifiedTemporaryDirectory {
    param([string]$Path, [string]$AllowedRoot)

    $resolved = Assert-TemporaryPath -Path $Path -AllowedRoot $AllowedRoot
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

$runRecords = [System.Collections.Generic.List[object]]::new()
$nextPortOffset = 0
$debugBuilt = [bool]$SkipDebugBuild

function Invoke-MatrixRun {
    param(
        [string]$Name,
        [string]$Expectation,
        [string]$Configuration,
        [string]$RuntimeKind,
        [string]$ExecutablePath,
        [double]$Scale,
        [string]$Route,
        [bool]$DeepCanvas,
        [bool]$NoBuild
    )

    $webPort = $BaseWebPort + $script:nextPortOffset
    $cdpPort = $BaseCdpPort + $script:nextPortOffset
    $script:nextPortOffset += 1
    $relativeEvidence = "$relativeEvidenceRoot/runs/$Name/evidence"
    $parameters = @{
        Expectation = $Expectation
        Configuration = $Configuration
        RuntimeKind = $RuntimeKind
        DesktopExecutablePath = $ExecutablePath
        NodeExecutablePath = $nodeExe
        RunName = $Name
        EvidenceDirectory = $relativeEvidence
        WebPort = $webPort
        CdpPort = $cdpPort
        Scale = $Scale
        SanitizeDesktopPath = $true
        RuntimeDirectory = Join-Path $runtimeDirectoryRoot "runs/$Name"
    }
    if (-not [string]::IsNullOrWhiteSpace($Route)) {
        $parameters["Route"] = $Route
    }
    if ($DeepCanvas) {
        $parameters["DeepCanvas"] = $true
    }
    if ($NoBuild) {
        $parameters["NoBuild"] = $true
    }

    $started = [DateTime]::UtcNow
    try {
        & $singleRun @parameters
        $runRecords.Add([pscustomobject]@{
            name = $Name
            expectation = $Expectation
            configuration = $Configuration
            runtimeKind = $RuntimeKind
            scale = $Scale
            webPort = $webPort
            cdpPort = $cdpPort
            status = "PASS"
            evidenceDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $relativeEvidence))
            startedAtUtc = $started.ToString("O")
            completedAtUtc = [DateTime]::UtcNow.ToString("O")
        })
    } catch {
        $runRecords.Add([pscustomobject]@{
            name = $Name
            expectation = $Expectation
            configuration = $Configuration
            runtimeKind = $RuntimeKind
            scale = $Scale
            webPort = $webPort
            cdpPort = $cdpPort
            status = "FAIL"
            error = $_.Exception.Message
            evidenceDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $relativeEvidence))
            startedAtUtc = $started.ToString("O")
            completedAtUtc = [DateTime]::UtcNow.ToString("O")
        })
        throw
    }
}

$matrixError = $null
$dpiStatus = "NOT_RUN"
$noNodeStatus = "NOT_RUN"
$performanceStatus = "NOT_RUN"
try {
    Invoke-MatrixRun `
        -Name "debug-legacy" `
        -Expectation "legacy" `
        -Configuration "Debug" `
        -RuntimeKind "debug" `
        -ExecutablePath $debugExe `
        -Scale 1.0 `
        -Route "" `
        -DeepCanvas $false `
        -NoBuild $debugBuilt
    $debugBuilt = $true

    Invoke-MatrixRun -Name "debug-diagnostics" -Expectation "studio-diagnostics" `
        -Configuration "Debug" -RuntimeKind "debug" -ExecutablePath $debugExe `
        -Scale 1.0 -Route "/diagnostics" -DeepCanvas $false -NoBuild $true
    Invoke-MatrixRun -Name "debug-design" -Expectation "studio-design" `
        -Configuration "Debug" -RuntimeKind "debug" -ExecutablePath $debugExe `
        -Scale 1.0 -Route "/labs/design" -DeepCanvas $false -NoBuild $true

    foreach ($scale in @(1.0, 1.25, 1.5, 2.0)) {
        $scaleName = ([string]$scale).Replace('.', '-')
        Invoke-MatrixRun -Name "debug-canvas-dpi-$scaleName" -Expectation "studio-canvas" `
            -Configuration "Debug" -RuntimeKind "debug" -ExecutablePath $debugExe `
            -Scale $scale -Route "/labs/canvas" -DeepCanvas ($scale -eq 1.0) -NoBuild $true
    }

    if (-not $SkipPerformance) {
        & $performanceRun `
            -Configuration "Debug" `
            -RuntimeKind "debug" `
            -DesktopExecutablePath $debugExe `
            -NodeExecutablePath $nodeExe `
            -RunName "$RunName-performance" `
            -EvidenceDirectory "$relativeEvidenceRoot/performance" `
            -RuntimeDirectory (Join-Path $runtimeDirectoryRoot "performance") `
            -GroupCount $PerformanceGroups `
            -BaseWebPort ($BaseWebPort + 100) `
            -BaseCdpPort ($BaseCdpPort + 100) `
            -SanitizeDesktopPath `
            -NoBuild
        $performanceSummary = Get-Content -Raw -LiteralPath (
            Join-Path $evidenceRoot "performance/studio-ui-canvas-performance-summary.json") |
            ConvertFrom-Json
        $performanceStatus = [string]$performanceSummary.decision
    }

    if (-not $SkipPublish) {
        if (Test-Path -LiteralPath $publishRoot) {
            throw "Temporary publish root already exists: $publishRoot"
        }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $publishRoot) | Out-Null
        & $dotnetRunner -ReturnExitCode publish $desktopProject `
            -c Release `
            --runtime win-x64 `
            --self-contained true `
            --output $publishRoot `
            --artifacts-path $publishArtifactsRoot
        $publishExitCode = $LASTEXITCODE
        if ($publishExitCode -ne 0) {
            throw "Release self-contained publish failed with exit code $publishExitCode."
        }
        $releaseExe = Join-Path $publishRoot "ClearVision.Product.Desktop.exe"
        Invoke-MatrixRun -Name "publish-diagnostics" -Expectation "studio-diagnostics" `
            -Configuration "Release" -RuntimeKind "publish" -ExecutablePath $releaseExe `
            -Scale 1.0 -Route "/diagnostics" -DeepCanvas $false -NoBuild $true
        Invoke-MatrixRun -Name "publish-canvas" -Expectation "studio-canvas" `
            -Configuration "Release" -RuntimeKind "publish" -ExecutablePath $releaseExe `
            -Scale 1.0 -Route "/labs/canvas" -DeepCanvas $false -NoBuild $true

        $verifiedMissingRoot = Assert-TemporaryPath `
            -Path $missingAssetsRoot `
            -AllowedRoot $publishCheckRoot
        if (Test-Path -LiteralPath $verifiedMissingRoot) {
            throw "Missing-assets publish sample already exists: $verifiedMissingRoot"
        }
        Copy-Item -LiteralPath $publishRoot -Destination $verifiedMissingRoot -Recurse
        $studioAssets = Assert-TemporaryPath `
            -Path (Join-Path $verifiedMissingRoot "wwwroot/studio") `
            -AllowedRoot $verifiedMissingRoot
        if (-not (Test-Path -LiteralPath $studioAssets -PathType Container)) {
            throw "Copied publish sample did not contain wwwroot/studio."
        }
        Remove-Item -LiteralPath $studioAssets -Recurse -Force
        Invoke-MatrixRun -Name "publish-missing-assets" -Expectation "missing-assets" `
            -Configuration "Release" -RuntimeKind "missing-assets" `
            -ExecutablePath (Join-Path $verifiedMissingRoot "ClearVision.Product.Desktop.exe") `
            -Scale 1.0 -Route "" -DeepCanvas $false -NoBuild $true
    }

    & $dpiAudit `
        -RuntimeEvidenceDirectory $evidenceRoot `
        -OutputPath (Join-Path $evidenceRoot "studio-ui-dpi-evidence.json")
    $dpiStatus = "PASS"

    if (-not $SkipPublish) {
        & $noNodeAudit `
            -PublishDirectory $publishRoot `
            -RuntimeEvidenceDirectory $evidenceRoot `
            -OutputPath (Join-Path $evidenceRoot "studio-ui-no-node-evidence.json")
        $noNodeStatus = "PASS"
    }
} catch {
    $matrixError = $_
} finally {
    if (-not $KeepTemporaryPublish) {
        Remove-VerifiedTemporaryDirectory -Path $missingAssetsRoot -AllowedRoot $publishCheckRoot
        Remove-VerifiedTemporaryDirectory -Path $publishRoot -AllowedRoot $publishCheckRoot
        Remove-VerifiedTemporaryDirectory -Path $publishArtifactsRoot -AllowedRoot $publishCheckRoot
    }
    Remove-VerifiedTemporaryDirectory `
        -Path $runtimeDirectoryRoot `
        -AllowedRoot $runtimeCleanupParent
}

$manifest = [pscustomobject]@{
    schemaVersion = 1
    runName = $RunName
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    status = if ($matrixError) { "FAIL" } else { "PASS" }
    error = if ($matrixError) { $matrixError.Exception.Message } else { $null }
    evidenceDirectory = $evidenceRoot
    publishDirectoryRetained = [bool]$KeepTemporaryPublish
    publishDirectory = $publishRoot
    runtimeDirectory = $runtimeDirectoryRoot
    runtimeDirectoryRemoved = -not (Test-Path -LiteralPath $runtimeDirectoryRoot)
    runs = @($runRecords)
    performance = $performanceStatus
    dpiAuthority = $dpiStatus
    localNoNodeEvidence = $noNodeStatus
    cleanMachineWithoutNode = "NOT_PERFORMED"
}
$manifestPath = Join-Path $evidenceRoot "studio-ui-webview2-matrix.json"
[System.IO.File]::WriteAllText(
    $manifestPath,
    (($manifest | ConvertTo-Json -Depth 10) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

if ($matrixError) {
    throw $matrixError
}

$manifest | ConvertTo-Json -Depth 6
