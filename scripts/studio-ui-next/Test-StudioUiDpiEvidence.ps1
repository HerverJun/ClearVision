[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeEvidenceDirectory,
    [double[]]$ExpectedScales = @(1.0, 1.25, 1.5, 2.0),
    [string]$OutputPath,
    [switch]$NoThrow
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot "../.."))
$runtimeRoot = [System.IO.Path]::GetFullPath($RuntimeEvidenceDirectory)
$desktopRoot = Join-Path $repoRoot "ClearVision.Product/src/ClearVision.Product.Desktop"
$projectPath = Join-Path $desktopRoot "ClearVision.Product.Desktop.csproj"
$programPath = Join-Path $desktopRoot "Program.cs"
$manifestPath = Join-Path $desktopRoot "app.manifest"
if (-not (Test-Path -LiteralPath $runtimeRoot -PathType Container)) {
    throw "Runtime evidence directory was not found: $runtimeRoot"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $runtimeRoot "studio-ui-dpi-evidence.json"
} else {
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
}

[xml]$projectXml = Get-Content -Raw -LiteralPath $projectPath
$dpiProperties = @($projectXml.SelectNodes(
        "//*[local-name()='ApplicationHighDpiMode']") |
    ForEach-Object { [string]$_.InnerText } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { $_.Trim() })
$programText = Get-Content -Raw -LiteralPath $programPath
$manifestText = Get-Content -Raw -LiteralPath $manifestPath
$programOverrides = @([regex]::Matches($programText, 'SetHighDpiMode\s*\(') |
    ForEach-Object { $_.Value })
$manifestDpiDeclarations = @([regex]::Matches(
    $manifestText,
    '<\s*(dpiAware|dpiAwareness)\b',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase) |
    ForEach-Object { $_.Value })
$codeAuthorityPassed = $dpiProperties.Count -eq 1 -and
    $dpiProperties[0] -eq "PerMonitorV2" -and
    $programOverrides.Count -eq 0 -and
    $manifestDpiDeclarations.Count -eq 0

$runtimeDocuments = [System.Collections.Generic.List[object]]::new()
foreach ($file in Get-ChildItem -LiteralPath $runtimeRoot -Recurse -File -Filter "studio-ui-webview2-*.json") {
    if ($file.Name -match '-cleanup\.json$') {
        continue
    }
    try {
        $document = Get-Content -Raw -Encoding UTF8 -LiteralPath $file.FullName | ConvertFrom-Json
    } catch {
        continue
    }
    if (($document.PSObject.Properties.Name -contains "nativeRuntime") -and
        ($document.PSObject.Properties.Name -contains "browserDpi")) {
        $runtimeDocuments.Add([pscustomobject]@{
            path = $file.FullName
            document = $document
        })
    }
}

$layers = @($runtimeDocuments | ForEach-Object {
    $document = $_.document
    $js = $document.browserDpi.js
    $screenshot = $document.browserDpi.screenshotPixels
    $native = $document.nativeRuntime
    $canvasRuntime = $null
    $pointerHit = $null
    $canvasEvidenceSource = $null
    if (($document.PSObject.Properties.Name -contains "canvasPage") -and $document.canvasPage) {
        $canvasRuntime = $document.canvasPage.mounted.canvas.runtime
        $pointerHit = $document.canvasPage.pointerHit
        $canvasEvidenceSource = "internal-canvas-page"
    } elseif (($document.PSObject.Properties.Name -contains "productPage") -and
        $document.productPage -and
        ($document.productPage.PSObject.Properties.Name -contains "workspaceCanvasDpi") -and
        $document.productPage.workspaceCanvasDpi) {
        $canvasRuntime = $document.productPage.workspaceCanvasDpi.mounted.canvas.runtime
        $pointerHit = $document.productPage.workspaceCanvasDpi.pointerHit
        $canvasEvidenceSource = "formal-product-workspace"
    }
    $screenshotScaleX = if ([double]$js.innerWidth -gt 0) {
        [double]$screenshot.width / [double]$js.innerWidth
    } else {
        $null
    }
    $screenshotScaleY = if ([double]$js.innerHeight -gt 0) {
        [double]$screenshot.height / [double]$js.innerHeight
    } else {
        $null
    }
    $requestedScale = [double]$document.scale
    $forceScaleValues = @($native.descendants | ForEach-Object {
        $commandLine = [string]$_.commandLine
        foreach ($match in [regex]::Matches(
            $commandLine,
            '--force-device-scale-factor=(?<value>[0-9]+(?:\.[0-9]+)?)',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            [double]::Parse(
                $match.Groups['value'].Value,
                [System.Globalization.CultureInfo]::InvariantCulture)
        }
    })
    $forceScalePassed = @($forceScaleValues | Where-Object {
        [Math]::Abs([double]$_ - $requestedScale) -le 0.001
    }).Count -gt 0
    $jsScalePassed = [Math]::Abs([double]$js.devicePixelRatio - $requestedScale) -le 0.05
    $screenshotScalePassed = $null -ne $screenshotScaleX -and
        $null -ne $screenshotScaleY -and
        [Math]::Abs([double]$screenshotScaleX - [double]$js.devicePixelRatio) -le 0.05 -and
        [Math]::Abs([double]$screenshotScaleY - [double]$js.devicePixelRatio) -le 0.05
    $pointerHitPassed = $null -ne $pointerHit -and
        -not [string]::IsNullOrWhiteSpace([string]$pointerHit.id)
    $canvasPassed = if ($null -eq $canvasRuntime) {
        $null
    } else {
        [Math]::Abs([double]$canvasRuntime.dpr - [double]$js.devicePixelRatio) -le 0.01 -and
        [Math]::Abs(
            [double]$canvasRuntime.backingWidth -
            ([double]$canvasRuntime.logicalWidth * [double]$canvasRuntime.dpr)) -le 2 -and
        [Math]::Abs(
            [double]$canvasRuntime.backingHeight -
            ([double]$canvasRuntime.logicalHeight * [double]$canvasRuntime.dpr)) -le 2 -and
        $pointerHitPassed
    }
    [pscustomobject]@{
        evidence = $_.path
        expectation = [string]$document.expectation
        phase = [string]$document.phase
        canvasEvidenceSource = $canvasEvidenceSource
        requestedWebView2ForceScale = $requestedScale
        observedWebView2ForceScaleArguments = $forceScaleValues
        nativeAwareness = [pscustomobject]@{
            label = [string]$native.awareness.label
            isPerMonitorV2 = $native.awareness.isPerMonitorV2 -eq $true
            processAwareness = [string]$native.awareness.processAwareness
        }
        nativeWindow = [pscustomobject]@{
            dpi = [int]$native.nativeWindow.dpi
            scale = [double]$native.nativeWindow.scale
            windowRect = $native.nativeWindow.windowRect
            clientSize = $native.nativeWindow.clientSize
        }
        browser = [pscustomobject]@{
            jsDevicePixelRatio = [double]$js.devicePixelRatio
            cdpLayoutViewport = $document.browserDpi.cdp.cssLayoutViewport
            screenshotPixels = $screenshot
            screenshotScaleX = $screenshotScaleX
            screenshotScaleY = $screenshotScaleY
        }
        canvas = if ($null -eq $canvasRuntime) { $null } else {
            [pscustomobject]@{
                dpr = [double]$canvasRuntime.dpr
                logicalWidth = [double]$canvasRuntime.logicalWidth
                logicalHeight = [double]$canvasRuntime.logicalHeight
                backingWidth = [double]$canvasRuntime.backingWidth
                backingHeight = [double]$canvasRuntime.backingHeight
                pointerHit = $pointerHit
            }
        }
        checks = [pscustomobject]@{
            nativePerMonitorV2 = $native.awareness.isPerMonitorV2 -eq $true
            webView2CommandLineHasRequestedScale = $forceScalePassed
            requestedScaleMatchesJsDpr = $jsScalePassed
            screenshotPixelsMatchJsDpr = $screenshotScalePassed
            canvasDprAndHitTesting = $canvasPassed
        }
    }
})

$scaleCoverage = @($ExpectedScales | ForEach-Object {
    $expected = [double]$_
    $matches = @($layers | Where-Object {
        ($_.expectation -eq "studio-canvas" -or
            ($_.phase -eq "f04" -and
                $_.expectation -eq "studio-product" -and
                $_.canvasEvidenceSource -eq "formal-product-workspace")) -and
        [Math]::Abs([double]$_.requestedWebView2ForceScale - $expected) -le 0.001
    })
    [pscustomobject]@{
        scale = $expected
        evidenceCount = $matches.Count
        passed = $matches.Count -gt 0 -and
            @($matches | Where-Object {
                -not $_.checks.nativePerMonitorV2 -or
                -not $_.checks.webView2CommandLineHasRequestedScale -or
                -not $_.checks.requestedScaleMatchesJsDpr -or
                -not $_.checks.screenshotPixelsMatchJsDpr -or
                $_.checks.canvasDprAndHitTesting -ne $true
            }).Count -eq 0
        evidence = @($matches | ForEach-Object { $_.evidence })
    }
})
$runtimePassed = $layers.Count -gt 0 -and
    @($layers | Where-Object {
        -not $_.checks.nativePerMonitorV2 -or
        -not $_.checks.webView2CommandLineHasRequestedScale -or
        -not $_.checks.requestedScaleMatchesJsDpr -or
        -not $_.checks.screenshotPixelsMatchJsDpr
    }).Count -eq 0
$matrixPassed = @($scaleCoverage | Where-Object { -not $_.passed }).Count -eq 0
$passed = $codeAuthorityPassed -and $runtimePassed -and $matrixPassed

$report = [pscustomobject]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    status = if ($passed) { "PASS" } else { "BLOCKED" }
    codeAuthority = [pscustomobject]@{
        project = $projectPath
        applicationHighDpiModeValues = $dpiProperties
        programSetHighDpiModeCalls = $programOverrides
        manifestDpiDeclarations = $manifestDpiDeclarations
        passed = $codeAuthorityPassed
    }
    requiredLayers = @(
        "project-property",
        "runtime-awareness",
        "webview2-force-scale",
        "browser-cdp-layout",
        "javascript-devicePixelRatio",
        "native-window-size",
        "canvas-backing-store-and-hit-testing"
    )
    expectedScaleMatrix = $scaleCoverage
    runtimeLayers = $layers
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
[System.IO.File]::WriteAllText(
    $OutputPath,
    (($report | ConvertTo-Json -Depth 14) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

if (-not $passed -and -not $NoThrow) {
    throw "StudioUI DPI authority evidence is blocked. See $OutputPath"
}

$report | ConvertTo-Json -Depth 8
