[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,
    [Parameter(Mandatory = $true)]
    [string]$RuntimeEvidenceDirectory,
    [string[]]$RequiredProductRoutes = @(),
    [ValidateSet("", "f01", "f02", "f03", "f04")]
    [string]$ExpectedEvidencePhase = "",
    [string]$ExpectedSourceSha = "",
    [string]$OutputPath,
    [switch]$NoThrow
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$publishRoot = [System.IO.Path]::GetFullPath($PublishDirectory)
$runtimeRoot = [System.IO.Path]::GetFullPath($RuntimeEvidenceDirectory)
if (-not [string]::IsNullOrWhiteSpace($ExpectedSourceSha) -and
    $ExpectedSourceSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw "ExpectedSourceSha must contain a 40-character commit SHA."
}
if (-not (Test-Path -LiteralPath $publishRoot -PathType Container)) {
    throw "Publish directory was not found: $publishRoot"
}
if (-not (Test-Path -LiteralPath $runtimeRoot -PathType Container)) {
    throw "Runtime evidence directory was not found: $runtimeRoot"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $runtimeRoot "studio-ui-no-node-evidence.json"
} else {
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
}
$publishPrefix = $publishRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar

function Get-RelativePublishPath {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith(
        $publishPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Publish audit path escaped its root: $fullPath"
    }
    return $fullPath.Substring($publishPrefix.Length).Replace('\', '/')
}

$publishEntries = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -Force)
$forbidden = [System.Collections.Generic.List[object]]::new()
foreach ($entry in $publishEntries) {
    $relative = Get-RelativePublishPath -Path $entry.FullName
    $segments = @($relative -split '/')
    $reason = $null
    if ($segments -contains "v2") {
        $reason = "retired-v2"
    } elseif ($segments -contains "node_modules") {
        $reason = "node-modules"
    } elseif ($segments -contains "StudioUI") {
        $reason = "studio-ui-source-tree"
    } elseif ($segments | Where-Object { $_ -match '^(tests?|fixtures?)$' }) {
        $reason = "test-or-browser-fixture"
    } elseif ($entry.Name -match '^(package-lock\.json|npm-debug\.log)$') {
        $reason = "node-package-artifact"
    } elseif (-not $entry.PSIsContainer -and $entry.Extension -match '^\.(ts|tsx|vue)$') {
        $reason = "frontend-source"
    } elseif ($entry.Name -match '^(package\.json|tsconfig(?:\.[^.]+)?\.json|vite\.config\.[^.]+|vitest\.config\.[^.]+|eslint\.config\.[^.]+)$') {
        $reason = "frontend-build-config"
    } elseif ($entry.Name -match '^(node(?:\.exe|\.dll)?|npm(?:\.cmd|\.exe)?|npx(?:\.cmd|\.exe)?)$') {
        $reason = "node-runtime-artifact"
    } elseif ($entry.Name -match '\.map$') {
        $reason = "source-map"
    } elseif ($relative -match '(^|/)(\.npm|_cacache)(/|$)') {
        $reason = "npm-cache"
    } elseif ($relative -match '(?i)(playwright|studio-ui-next-server|browser-fixture)') {
        $reason = "browser-test-artifact"
    }
    if ($reason) {
        $forbidden.Add([pscustomobject]@{
            path = $relative
            kind = if ($entry.PSIsContainer) { "directory" } else { "file" }
            reason = $reason
        })
    }
}

$requiredPaths = [ordered]@{
    desktopExecutable = Join-Path $publishRoot "ClearVision.Product.Desktop.exe"
    legacyIndex = Join-Path $publishRoot "wwwroot/index.html"
    studioIndex = Join-Path $publishRoot "wwwroot/studio/index.html"
    studioAssets = Join-Path $publishRoot "wwwroot/studio/assets"
    studioManifest = Join-Path $publishRoot "wwwroot/studio/.vite/manifest.json"
}
$requiredChecks = [ordered]@{}
foreach ($entry in $requiredPaths.GetEnumerator()) {
    $requiredChecks[$entry.Key] = Test-Path -LiteralPath $entry.Value
}

$studioIndexPath = $requiredPaths.studioIndex
$studioManifestPath = $requiredPaths.studioManifest
$indexAssetChecks = @()
$manifestAssetChecks = @()
$devSignatureChecks = [System.Collections.Generic.List[object]]::new()
if ($requiredChecks.studioIndex) {
    $indexContent = Get-Content -Raw -LiteralPath $studioIndexPath
    $indexReferences = @([regex]::Matches(
        $indexContent,
        '(?i)(?:src|href)\s*=\s*["''](?<path>[^"'']+)["'']') |
        ForEach-Object { $_.Groups['path'].Value } |
        Where-Object { $_ -notmatch '^(?:data:|#)' } |
        Sort-Object -Unique)
    $indexAssetChecks = @($indexReferences | ForEach-Object {
        $reference = [string]$_
        $isStudioAsset = $reference -match '^/studio/assets/[^/?#]+\.(?:js|css)(?:[?#].*)?$'
        $relativeAsset = ($reference -replace '[?#].*$', '').TrimStart('/').Replace('/', '\')
        $resolvedAsset = Join-Path (Join-Path $publishRoot 'wwwroot') $relativeAsset
        [pscustomobject]@{
            reference = $reference
            usesStudioBasePath = $isStudioAsset
            exists = $isStudioAsset -and (Test-Path -LiteralPath $resolvedAsset -PathType Leaf)
            resolvedPath = $resolvedAsset
            passed = $isStudioAsset -and (Test-Path -LiteralPath $resolvedAsset -PathType Leaf)
        }
    })
}
if ($requiredChecks.studioManifest) {
    try {
        $manifest = Get-Content -Raw -LiteralPath $studioManifestPath | ConvertFrom-Json
        $manifestAssets = [System.Collections.Generic.List[string]]::new()
        foreach ($manifestEntry in $manifest.PSObject.Properties) {
            $value = $manifestEntry.Value
            if ($value.PSObject.Properties.Name -contains 'file') {
                $manifestAssets.Add([string]$value.file)
            }
            if ($value.PSObject.Properties.Name -contains 'css') {
                foreach ($cssPath in @($value.css)) {
                    $manifestAssets.Add([string]$cssPath)
                }
            }
        }
        $manifestAssetChecks = @($manifestAssets | Sort-Object -Unique | ForEach-Object {
            $reference = [string]$_
            $usesAssetsDirectory = $reference -match '^assets/[^/?#]+\.(?:js|css)$'
            $resolvedAsset = Join-Path (Join-Path $publishRoot 'wwwroot/studio') (
                $reference.Replace('/', '\'))
            [pscustomobject]@{
                reference = $reference
                usesAssetsDirectory = $usesAssetsDirectory
                exists = $usesAssetsDirectory -and (Test-Path -LiteralPath $resolvedAsset -PathType Leaf)
                resolvedPath = $resolvedAsset
                passed = $usesAssetsDirectory -and (Test-Path -LiteralPath $resolvedAsset -PathType Leaf)
            }
        })
    } catch {
        $manifestAssetChecks = @([pscustomobject]@{
            reference = $null
            usesAssetsDirectory = $false
            exists = $false
            resolvedPath = $studioManifestPath
            passed = $false
            error = $_.Exception.Message
        })
    }
}

$devSignatures = [ordered]@{
    viteClient = '(?i)(?:/@vite/client|__vite_ping|vite-hmr)'
    sourceModule = '(?i)(?:/src/[^"'']+\.(?:ts|tsx|vue)|localhost:517[0-9])'
    hotReload = '(?i)(?:import\.meta\.hot|react-refresh)'
}
foreach ($assetFile in Get-ChildItem -LiteralPath (Join-Path $publishRoot 'wwwroot/studio') `
    -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -match '^\.(html|js|css)$' }) {
    $content = Get-Content -Raw -LiteralPath $assetFile.FullName
    foreach ($signature in $devSignatures.GetEnumerator()) {
        if ($content -match $signature.Value) {
            $devSignatureChecks.Add([pscustomobject]@{
                path = Get-RelativePublishPath -Path $assetFile.FullName
                signature = $signature.Key
            })
        }
    }
}

$indexPathsPassed = $indexAssetChecks.Count -gt 0 -and
    @($indexAssetChecks | Where-Object { -not $_.passed }).Count -eq 0
$manifestPathsPassed = $manifestAssetChecks.Count -gt 0 -and
    @($manifestAssetChecks | Where-Object { -not $_.passed }).Count -eq 0
$devAssetsPassed = $devSignatureChecks.Count -eq 0
$staticPassed = $forbidden.Count -eq 0 -and
    @($requiredChecks.Values | Where-Object { $_ -ne $true }).Count -eq 0 -and
    $indexPathsPassed -and $manifestPathsPassed -and $devAssetsPassed

$runtimeDocuments = [System.Collections.Generic.List[object]]::new()
foreach ($file in Get-ChildItem -LiteralPath $runtimeRoot -Recurse -File -Filter "*.json") {
    try {
        $document = Get-Content -Raw -Encoding UTF8 -LiteralPath $file.FullName | ConvertFrom-Json
    } catch {
        continue
    }
    if ($document.PSObject.Properties.Name -contains "nativeRuntime") {
        $runtimeDocuments.Add([pscustomobject]@{
            path = $file.FullName
            document = $document
        })
    }
}
$normalizedRequiredRoutes = @($RequiredProductRoutes |
    ForEach-Object {
        $route = ([string]$_).Trim()
        if ([string]::IsNullOrWhiteSpace($route)) {
            return
        }
        if (-not $route.StartsWith('/')) {
            $route = "/$route"
        }
        $route
    } |
    Sort-Object -Unique)
$publishedProductDocuments = @($runtimeDocuments | Where-Object {
    $names = $_.document.PSObject.Properties.Name
    $names -contains 'runtimeKind' -and
        $names -contains 'expectation' -and
        $names -contains 'route' -and
        $_.document.runtimeKind -eq 'publish' -and
        $_.document.expectation -eq 'studio-product'
})
$publishedProductChecks = @($normalizedRequiredRoutes | ForEach-Object {
    $requiredRoute = [string]$_
    $match = @($publishedProductDocuments | Where-Object {
        [string]$_.document.route -eq $requiredRoute
    } | Select-Object -First 1)
    if ($match.Count -eq 0) {
        [pscustomobject]@{
            route = $requiredRoute
            evidence = $null
            passed = $false
            reason = 'missing-published-product-evidence'
        }
        return
    }

    $item = $match[0]
    $document = $item.document
    $documentProperties = $document.PSObject.Properties.Name
    $hasExpectedPhase = [string]::IsNullOrWhiteSpace($ExpectedEvidencePhase) -or
        ($documentProperties -contains 'phase' -and
            [string]$document.phase -eq $ExpectedEvidencePhase)
    $hasExpectedSourceSha = [string]::IsNullOrWhiteSpace($ExpectedSourceSha) -or
        ($documentProperties -contains 'sourceSha' -and
            [string]$document.sourceSha -eq $ExpectedSourceSha)
    $startup = $document.studio.startup
    $startupValue = $startup.value
    $startupPassed = $startup.exists -eq $true -and
        $startup.frozen -eq $true -and
        $startup.featureFlagsFrozen -eq $true -and
        $startup.tokenPresent -eq $true -and
        $startup.chromeWebView -eq $true -and
        [int]$startupValue.schemaVersion -eq 1 -and
        [string]$startupValue.uiKind -eq 'studio-ui' -and
        [string]$startupValue.hostKind -eq 'desktop-webview2' -and
        [string]$startupValue.studioUiBasePath -eq '/studio/'
    $productPage = $document.productPage
    $productMethods = @($productPage.productRequests | ForEach-Object { [string]$_.method })
    $preferenceMethods = @($productPage.preferenceRequests | ForEach-Object { [string]$_.method })
    $allProductMethods = @($productMethods) + @($preferenceMethods)
    $getOnlyPassed = @($allProductMethods |
        Where-Object { $_ -ne 'GET' }).Count -eq 0 -and
        @($productPage.writeRequests).Count -eq 0 -and
        @($productPage.preferenceWriteRequests).Count -eq 0
    $preferences = $productPage.preferenceCycle
    $preferencesPassed = [string]$preferences.initial.theme -eq 'light' -and
        [string]$preferences.initial.density -eq 'compact' -and
        [string]$preferences.dark.attributeValue -eq 'dark' -and
        [string]$preferences.comfortable.attributeValue -eq 'comfortable' -and
        [string]$preferences.final.theme -eq 'light' -and
        [string]$preferences.final.density -eq 'compact' -and
        [string]$preferences.final.stored.theme -eq 'light' -and
        [string]$preferences.final.stored.density -eq 'compact'
    $runtimeErrorsPassed = @($document.runtimeErrors.consoleErrors).Count -eq 0 -and
        @($document.runtimeErrors.pageErrors).Count -eq 0 -and
        @($document.meaningfulRequestFailures).Count -eq 0
    $desktopExecutable = [System.IO.Path]::GetFullPath(
        [string]$document.nativeRuntime.desktop.executablePath)
    $publishedExecutablePassed = $desktopExecutable.StartsWith(
        $publishPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)
    $authorityPassed = [string]$productPage.dataSource -eq 'REAL_WEBVIEW2_EMPTY_AUTHORITY' -and
        [string]$productPage.authSource -eq 'HARNESS_SEEDED_SESSION'
    $screenshot = $document.viewportScreenshot
    $screenshotPath = [System.IO.Path]::GetFullPath([string]$screenshot.path)
    $screenshotExists = Test-Path -LiteralPath $screenshotPath -PathType Leaf
    $screenshotHash = if ($screenshotExists) {
        (Get-FileHash -LiteralPath $screenshotPath -Algorithm SHA256).Hash.ToLowerInvariant()
    } else {
        $null
    }
    $requiredScene = if ($requiredRoute -eq '/overview') {
        'app-shell'
    } else {
        $requiredRoute.TrimStart('/')
    }
    $screenshotPassed = $screenshotExists -and
        [string]$screenshot.sourceSha -eq [string]$document.sourceSha -and
        [string]$screenshot.DATA_SOURCE -eq 'REAL_WEBVIEW2_EMPTY_AUTHORITY' -and
        [string]$screenshot.AUTH_SOURCE -eq 'HARNESS_SEEDED_SESSION' -and
        [string]$screenshot.route -eq $requiredRoute -and
        [string]$screenshot.theme -eq 'light' -and
        [string]$screenshot.density -eq 'compact' -and
        @($screenshot.scenes) -contains $requiredScene -and
        [string]$screenshot.DPR_TYPE -eq 'WEBVIEW2_FORCE_DEVICE_SCALE_FACTOR' -and
        [string]$screenshot.DPI_TYPE -eq 'NATIVE_WINDOW_DPI_OBSERVED' -and
        [int]$screenshot.nativeWindow.windowRect.width -gt 0 -and
        [int]$screenshot.nativeWindow.windowRect.height -gt 0 -and
        [double]$screenshot.browserViewport.devicePixelRatio -gt 0 -and
        [string]$screenshot.sha256 -eq $screenshotHash
    $passed = [string]$document.status -eq 'pass' -and
        $hasExpectedPhase -and $hasExpectedSourceSha -and
        $startupPassed -and $getOnlyPassed -and
        $preferencesPassed -and $runtimeErrorsPassed -and
        $publishedExecutablePassed -and $authorityPassed -and $screenshotPassed
    [pscustomobject]@{
        route = $requiredRoute
        evidence = $item.path
        sourceSha = [string]$document.sourceSha
        sourceShaPassed = $hasExpectedSourceSha
        evidencePhase = [string]$document.phase
        startupConfigPassed = $startupPassed
        preseededSessionPassed = $startup.tokenPresent -eq $true -and $authorityPassed
        getOnlyPassed = $getOnlyPassed
        themeDensityPassed = $preferencesPassed
        consoleAndPageErrorsZero = $runtimeErrorsPassed
        screenshotPassed = $screenshotPassed
        screenshotPath = $screenshotPath
        screenshotSha256 = $screenshotHash
        screenshotScenes = @($screenshot.scenes)
        screenshotDataSource = [string]$screenshot.DATA_SOURCE
        screenshotAuthSource = [string]$screenshot.AUTH_SOURCE
        screenshotTheme = [string]$screenshot.theme
        screenshotDensity = [string]$screenshot.density
        screenshotNativeWindow = $screenshot.nativeWindow
        screenshotBrowserViewport = $screenshot.browserViewport
        screenshotDprType = [string]$screenshot.DPR_TYPE
        screenshotDpiType = [string]$screenshot.DPI_TYPE
        publishedExecutablePath = $desktopExecutable
        launchedFromPublishDirectory = $publishedExecutablePassed
        passed = $passed
    }
})
$publishedProductPassed = $normalizedRequiredRoutes.Count -eq 0 -or
    ($publishedProductChecks.Count -eq $normalizedRequiredRoutes.Count -and
        @($publishedProductChecks | Where-Object { -not $_.passed }).Count -eq 0)
$processTreeChecks = @($runtimeDocuments | ForEach-Object {
    [pscustomobject]@{
        evidence = $_.path
        status = [string]$_.document.status
        desktopProcessId = $_.document.nativeRuntime.desktop.processId
        descendantCount = @($_.document.nativeRuntime.descendants).Count
        nodeDescendantCount = [int]$_.document.nativeRuntime.nodeDescendantCount
        passed = $_.document.status -eq "pass" -and
            [int]$_.document.nativeRuntime.nodeDescendantCount -eq 0
    }
})
$processTreePassed = $processTreeChecks.Count -gt 0 -and
    @($processTreeChecks | Where-Object { -not $_.passed }).Count -eq 0

$cleanupDocuments = [System.Collections.Generic.List[object]]::new()
foreach ($file in Get-ChildItem -LiteralPath $runtimeRoot -Recurse -File -Filter "*-cleanup.json") {
    try {
        $document = Get-Content -Raw -Encoding UTF8 -LiteralPath $file.FullName | ConvertFrom-Json
    } catch {
        continue
    }
    $cleanupDocuments.Add([pscustomobject]@{
        path = $file.FullName
        document = $document
    })
}
$sanitizedChecks = @($cleanupDocuments |
    Where-Object { $_.document.sanitizedDesktopPath -eq $true } |
    ForEach-Object {
        [pscustomobject]@{
            evidence = $_.path
            runnerSucceeded = $_.document.runnerSucceeded -eq $true
            cleanupPassed = $_.document.passed -eq $true
            processCleanupPassed = $_.document.processCleanup.passed -eq $true
            portCleanupPassed = if (
                $_.document.PSObject.Properties.Name -contains 'portCleanup') {
                $_.document.portCleanup.passed -eq $true
            } else {
                $normalizedRequiredRoutes.Count -eq 0
            }
            runtimeRootRemoved = $_.document.runtimeCleanup.runtimeRootRemoved -eq $true
            externalNodeDriver = $_.document.externalNodeDriver
            passed = $_.document.runnerSucceeded -eq $true -and
                $_.document.passed -eq $true -and
                $_.document.processCleanup.passed -eq $true -and
                ($_.document.PSObject.Properties.Name -contains 'portCleanup' -and
                    $_.document.portCleanup.passed -eq $true -or
                    $normalizedRequiredRoutes.Count -eq 0) -and
                $_.document.runtimeCleanup.runtimeRootRemoved -eq $true
        }
    })
$sanitizedPathPassed = $sanitizedChecks.Count -gt 0 -and
    @($sanitizedChecks | Where-Object { -not $_.passed }).Count -eq 0

$externalDriverChecks = @($runtimeDocuments | ForEach-Object {
    $driver = $_.document.externalDriver
    [pscustomobject]@{
        evidence = $_.path
        executablePath = [string]$driver.executablePath
        role = [string]$driver.role
        insideDesktopProcessTree = $driver.insideDesktopProcessTree
        passed = [System.IO.Path]::IsPathRooted([string]$driver.executablePath) -and
            [string]$driver.role -eq "external-cdp-driver" -and
            $driver.insideDesktopProcessTree -eq $false
    }
})
$externalDriverRecorded = $externalDriverChecks.Count -gt 0 -and
    @($externalDriverChecks | Where-Object { -not $_.passed }).Count -eq 0

$localPassed = $staticPassed -and $processTreePassed -and
    $sanitizedPathPassed -and $externalDriverRecorded -and
    $publishedProductPassed
$report = [pscustomobject]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    publishDirectory = $publishRoot
    runtimeEvidenceDirectory = $runtimeRoot
    publishStaticScan = [pscustomobject]@{
        status = if ($staticPassed) { "PASS" } else { "BLOCKED" }
        requiredPaths = $requiredChecks
        indexAssetPaths = [pscustomobject]@{
            status = if ($indexPathsPassed) { "PASS" } else { "BLOCKED" }
            checks = $indexAssetChecks
        }
        manifestAssetPaths = [pscustomobject]@{
            status = if ($manifestPathsPassed) { "PASS" } else { "BLOCKED" }
            checks = $manifestAssetChecks
        }
        sourceMapAndDevAssets = [pscustomobject]@{
            status = if ($devAssetsPassed -and
                @($forbidden | Where-Object { $_.reason -eq 'source-map' }).Count -eq 0) {
                "PASS"
            } else {
                "BLOCKED"
            }
            devSignatureMatches = @($devSignatureChecks)
        }
        forbiddenArtifactCount = $forbidden.Count
        forbiddenArtifacts = @($forbidden)
    }
    publishedProductRuntime = [pscustomobject]@{
        status = if ($normalizedRequiredRoutes.Count -eq 0) {
            "NOT_REQUESTED"
        } elseif ($publishedProductPassed) {
            "PASS"
        } else {
            "BLOCKED"
        }
        expectedEvidencePhase = $ExpectedEvidencePhase
        expectedSourceSha = $ExpectedSourceSha
        requiredRoutes = $normalizedRequiredRoutes
        checks = $publishedProductChecks
    }
    desktopChildProcessAudit = [pscustomobject]@{
        status = if ($processTreePassed) { "PASS" } else { "BLOCKED" }
        evidenceCount = $processTreeChecks.Count
        checks = $processTreeChecks
    }
    sanitizedPathDesktopStartup = [pscustomobject]@{
        status = if ($sanitizedPathPassed) { "PASS" } else { "BLOCKED" }
        evidenceCount = $sanitizedChecks.Count
        checks = $sanitizedChecks
    }
    externalCdpDriver = [pscustomobject]@{
        status = if ($externalDriverRecorded) { "FACT_RECORDED" } else { "BLOCKED" }
        note = "The absolute Node executable drives CDP outside the Desktop process tree and is not clean-target evidence."
        checks = $externalDriverChecks
    }
    cleanMachineWithoutNode = [pscustomobject]@{
        status = "NOT_PERFORMED"
        note = "A separate target machine on which Node is not installed was not exercised by this local audit."
    }
    localNoNodeEvidence = if ($localPassed) { "PASS" } else { "BLOCKED" }
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
[System.IO.File]::WriteAllText(
    $OutputPath,
    (($report | ConvertTo-Json -Depth 12) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

if (-not $localPassed -and -not $NoThrow) {
    throw "Local no-Node evidence is blocked. See $OutputPath"
}

$report | ConvertTo-Json -Depth 8
