[CmdletBinding()]
param(
    [string]$NodeExecutablePath,
    [string]$DebugDesktopExecutablePath,
    [string]$RunName,
    [string]$EvidenceDirectory,
    [string]$RuntimeDirectory,
    [string]$PublishDirectory,
    [ValidateSet("f01", "f02")]
    [string]$EvidencePhase = "f01",
    [ValidateSet("full", "publish-only")]
    [string]$RunScope = "full",
    [int]$BaseWebPort = 5300,
    [int]$BaseCdpPort = 9623,
    [int]$WindowWidth = 1600,
    [int]$WindowHeight = 1000,
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
$sourceSha = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceSha)) {
    throw "Could not resolve the source SHA for the WebView2 matrix."
}
if ($sourceSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw "The WebView2 matrix source SHA is not a 40-character commit SHA."
}
if ($RunScope -eq "publish-only" -and $SkipPublish) {
    throw "RunScope=publish-only cannot be combined with SkipPublish."
}

if ([string]::IsNullOrWhiteSpace($RunName)) {
    $RunName = "webview2-matrix-{0}" -f [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss-fff")
}
$RunName = ($RunName -replace '[^A-Za-z0-9_.-]+', '-').Trim('-')
if ([string]::IsNullOrWhiteSpace($RunName)) {
    throw "RunName must contain at least one safe filename character."
}

$relativeEvidenceRoot = if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    ".tmp/studio-ui-next/$EvidencePhase/matrix/$RunName"
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
    Join-Path $repoRoot ".tmp/studio-ui-next/$EvidencePhase/runtime/$RunName"
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
    Join-Path $repoRoot ".tmp/publish-check/studio-ui-next-$EvidencePhase/$RunName/publish"
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
$publishLockFilePath = Join-Path $publishArtifactsRoot 'locks\restore-disabled.packages.lock.json'
$publishProductRoutes = if ($EvidencePhase -eq "f02") {
    @("/overview", "/projects", "/operators", "/stations", "/results")
} else {
    @("/overview")
}
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
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        if (-not (Test-Path -LiteralPath $resolved)) {
            return
        }
        $extendedPath = if ($resolved.StartsWith('\\')) {
            '\\?\UNC\' + $resolved.TrimStart('\')
        } else {
            '\\?\' + $resolved
        }
        try {
            [System.IO.Directory]::Delete($extendedPath, $true)
            if (-not (Test-Path -LiteralPath $resolved)) {
                return
            }
            throw "Directory deletion completed without removing '$resolved'."
        } catch {
            if ($attempt -eq 20) {
                throw
            }
            Start-Sleep -Milliseconds 250
        }
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
        EvidencePhase = $EvidencePhase
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
        WindowWidth = $WindowWidth
        WindowHeight = $WindowHeight
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
$releaseBuildStatus = "NOT_RUN"
$publishStatus = "NOT_RUN"
$publishStaticAuditStatus = "NOT_RUN"
$publishedProductRuntimeStatus = "NOT_RUN"
$viewportScreenshotIndex = @()
try {
    if ($RunScope -eq "full") {
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
        Invoke-MatrixRun -Name "debug-overview" -Expectation "studio-product" `
            -Configuration "Debug" -RuntimeKind "debug" -ExecutablePath $debugExe `
            -Scale 1.0 -Route "/overview" -DeepCanvas $false -NoBuild $true
        Invoke-MatrixRun -Name "debug-projects" -Expectation "studio-product" `
            -Configuration "Debug" -RuntimeKind "debug" -ExecutablePath $debugExe `
            -Scale 1.0 -Route "/projects" -DeepCanvas $false -NoBuild $true
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
                -EvidencePhase $EvidencePhase `
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
    } else {
        $performanceStatus = "NOT_APPLICABLE_PUBLISH_ONLY"
        $dpiStatus = "NOT_APPLICABLE_PUBLISH_ONLY"
    }

    if (-not $SkipPublish) {
        if (Test-Path -LiteralPath $publishRoot) {
            throw "Temporary publish root already exists: $publishRoot"
        }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $publishRoot) | Out-Null
        if ($RunScope -eq "publish-only") {
            $releaseBuildArguments = @(
                "build",
                $desktopProject,
                "-c", "Release",
                "--runtime", "win-x64",
                "--self-contained", "true",
                "-p:RestorePackagesWithLockFile=false",
                "-p:NuGetLockFilePath=$publishLockFilePath",
                "--artifacts-path", $publishArtifactsRoot)
            & $dotnetRunner -ReturnExitCode -Arguments $releaseBuildArguments
            $releaseBuildExitCode = $LASTEXITCODE
            if ($releaseBuildExitCode -ne 0) {
                throw "StudioUI/Desktop Release build failed with exit code $releaseBuildExitCode."
            }
            $releaseBuildStatus = "PASS"
        } else {
            $releaseBuildStatus = "PASS_VIA_PUBLISH_BUILD_TARGET"
        }
        $publishArguments = @(
            "publish",
            $desktopProject,
            "-c", "Release",
            "--runtime", "win-x64",
            "--self-contained", "true",
            "-p:RestorePackagesWithLockFile=false",
            "-p:NuGetLockFilePath=$publishLockFilePath",
            "--output", $publishRoot,
            "--artifacts-path", $publishArtifactsRoot)
        & $dotnetRunner -ReturnExitCode -Arguments $publishArguments
        $publishExitCode = $LASTEXITCODE
        if ($publishExitCode -ne 0) {
            throw "Release self-contained publish failed with exit code $publishExitCode."
        }
        $publishStatus = "PASS"
        $releaseExe = Join-Path $publishRoot "ClearVision.Product.Desktop.exe"
        if ($RunScope -eq "full") {
            Invoke-MatrixRun -Name "publish-diagnostics" -Expectation "studio-diagnostics" `
                -Configuration "Release" -RuntimeKind "publish" -ExecutablePath $releaseExe `
                -Scale 1.0 -Route "/diagnostics" -DeepCanvas $false -NoBuild $true
        }
        foreach ($productRoute in $publishProductRoutes) {
            $routeName = $productRoute.Trim('/').Replace('/', '-')
            Invoke-MatrixRun -Name "publish-$routeName" -Expectation "studio-product" `
                -Configuration "Release" -RuntimeKind "publish" -ExecutablePath $releaseExe `
                -Scale 1.0 -Route $productRoute -DeepCanvas $false -NoBuild $true
        }
        if ($RunScope -eq "full") {
            Invoke-MatrixRun -Name "publish-canvas" -Expectation "studio-canvas" `
                -Configuration "Release" -RuntimeKind "publish" -ExecutablePath $releaseExe `
                -Scale 1.0 -Route "/labs/canvas" -DeepCanvas $false -NoBuild $true
        }

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

    if ($RunScope -eq "full") {
        & $dpiAudit `
            -RuntimeEvidenceDirectory $evidenceRoot `
            -OutputPath (Join-Path $evidenceRoot "studio-ui-dpi-evidence.json")
        $dpiStatus = "PASS"
    }

    if (-not $SkipPublish) {
        $noNodeParameters = @{
            PublishDirectory = $publishRoot
            RuntimeEvidenceDirectory = $evidenceRoot
            OutputPath = Join-Path $evidenceRoot "studio-ui-no-node-evidence.json"
        }
        if ($EvidencePhase -eq "f02") {
            $noNodeParameters["RequiredProductRoutes"] = $publishProductRoutes
            $noNodeParameters["ExpectedEvidencePhase"] = $EvidencePhase
            $noNodeParameters["ExpectedSourceSha"] = $sourceSha
        }
        & $noNodeAudit @noNodeParameters
        $noNodeDocument = Get-Content -Raw -Encoding UTF8 -LiteralPath (
            $noNodeParameters.OutputPath) | ConvertFrom-Json
        $publishStaticAuditStatus = [string]$noNodeDocument.publishStaticScan.status
        $publishedProductRuntimeStatus = [string]$noNodeDocument.publishedProductRuntime.status
        $viewportScreenshotIndex = @($noNodeDocument.publishedProductRuntime.checks |
            Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.screenshotPath) } |
            ForEach-Object {
                [pscustomobject]@{
                    route = [string]$_.route
                    path = [string]$_.screenshotPath
                    sha256 = [string]$_.screenshotSha256
                    sourceSha = [string]$_.sourceSha
                    scenes = @($_.screenshotScenes)
                    dataSource = [string]$_.screenshotDataSource
                    authSource = [string]$_.screenshotAuthSource
                    theme = [string]$_.screenshotTheme
                    density = [string]$_.screenshotDensity
                    nativeWindow = $_.screenshotNativeWindow
                    browserViewport = $_.screenshotBrowserViewport
                    dprType = [string]$_.screenshotDprType
                    dpiType = [string]$_.screenshotDpiType
                }
            })
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
    evidencePhase = $EvidencePhase
    sourceSha = $sourceSha
    runName = $RunName
    runScope = $RunScope
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    status = if ($matrixError) { "FAIL" } else { "PASS" }
    error = if ($matrixError) { $matrixError.Exception.Message } else { $null }
    evidenceDirectory = $evidenceRoot
    publishDirectoryRetained = [bool]$KeepTemporaryPublish
    publishDirectory = $publishRoot
    releaseBuild = $releaseBuildStatus
    releasePublish = $publishStatus
    publishStaticAudit = $publishStaticAuditStatus
    publishedProductRuntime = $publishedProductRuntimeStatus
    requiredPublishedProductRoutes = $publishProductRoutes
    viewportScreenshots = $viewportScreenshotIndex
    window = [pscustomobject]@{
        width = $WindowWidth
        height = $WindowHeight
    }
    portRange = [pscustomobject]@{
        baseWebPort = $BaseWebPort
        baseCdpPort = $BaseCdpPort
        count = $nextPortOffset
    }
    runtimeDirectory = $runtimeDirectoryRoot
    runtimeDirectoryRemoved = -not (Test-Path -LiteralPath $runtimeDirectoryRoot)
    publishCleanup = [pscustomobject]@{
        publishDirectoryRemoved = -not (Test-Path -LiteralPath $publishRoot)
        missingAssetsDirectoryRemoved = -not (Test-Path -LiteralPath $missingAssetsRoot)
        buildArtifactsDirectoryRemoved = -not (Test-Path -LiteralPath $publishArtifactsRoot)
    }
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
