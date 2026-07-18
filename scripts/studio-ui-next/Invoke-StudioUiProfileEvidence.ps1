[CmdletBinding()]
param(
    [string]$NodeExecutablePath,
    [string]$DesktopExecutablePath,
    [string]$RunName,
    [string]$EvidenceDirectory,
    [int]$BaseWebPort = 5700,
    [int]$BaseCdpPort = 9823,
    [switch]$NoBuild,
    [switch]$KeepMissingAssetsRuntime
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot "../.."))
$singleRun = Join-Path $scriptRoot "Invoke-StudioUiWebView2Evidence.ps1"
$nodeExe = if ([string]::IsNullOrWhiteSpace($NodeExecutablePath)) {
    (Get-Command node.exe -ErrorAction Stop).Source
} else {
    [System.IO.Path]::GetFullPath($NodeExecutablePath)
}
$desktopExe = if ([string]::IsNullOrWhiteSpace($DesktopExecutablePath)) {
    Join-Path $repoRoot (
        "ClearVision.Product/src/ClearVision.Product.Desktop/bin/Debug/" +
        "net8.0-windows/win-x64/ClearVision.Product.Desktop.exe")
} else {
    [System.IO.Path]::GetFullPath($DesktopExecutablePath)
}
$sourceSha = (& git -C $repoRoot rev-parse HEAD).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $sourceSha -notmatch '^[0-9a-f]{40}$') {
    throw "Could not resolve a 40-character source SHA for profile evidence."
}
if (-not (Test-Path -LiteralPath $singleRun -PathType Leaf)) {
    throw "The StudioUI WebView2 evidence wrapper was not found: $singleRun"
}
if (-not (Test-Path -LiteralPath $nodeExe -PathType Leaf)) {
    throw "The Node evidence driver was not found: $nodeExe"
}

if ([string]::IsNullOrWhiteSpace($RunName)) {
    $RunName = "g5-profiles-{0}" -f [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss-fff")
}
$RunName = ($RunName -replace '[^A-Za-z0-9_.-]+', '-').Trim('-')
if ([string]::IsNullOrWhiteSpace($RunName)) {
    throw "RunName must contain at least one safe filename character."
}
if ($BaseWebPort -lt 1 -or $BaseWebPort + 7 -gt 65535) {
    throw "BaseWebPort must leave room for eight isolated runs."
}
if ($BaseCdpPort -lt 1 -or $BaseCdpPort + 7 -gt 65535) {
    throw "BaseCdpPort must leave room for eight isolated runs."
}

$relativeEvidenceRoot = if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    ".tmp/studio-ui-next/f04/profiles/$RunName"
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
    throw "Profile evidence must remain under .tmp/studio-ui-next."
}
if (Test-Path -LiteralPath $evidenceRoot) {
    throw "Profile evidence root already exists; use a unique RunName: $evidenceRoot"
}
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null

$publishCheckBoundary = [System.IO.Path]::GetFullPath((
    Join-Path $repoRoot ".tmp/publish-check/studio-ui-next-f04"))
$publishCheckRoot = Join-Path $publishCheckBoundary $RunName
$missingAssetsRuntime = Join-Path $publishCheckRoot "missing-assets-runtime"

function Remove-VerifiedTemporaryDirectory {
    param([string]$Path, [string]$AllowedRoot)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    $resolved = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetFullPath($AllowedRoot)
    $prefix = $root.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe temporary directory '$resolved'; expected a child of '$root'."
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

$evidenceSummaryScript = @'
const fs = require('fs');
const evidence = JSON.parse(fs.readFileSync(process.argv[1], 'utf8'));
const ledger = evidence.productPage?.ownerLedger || null;
const legacy = evidence.legacy?.projection || null;
const missing = evidence.missingAssets || null;
const rootKind = legacy ? 'legacy' : missing ? 'diagnostic' : 'studio-ui';
const owners = legacy || missing ? {
  studioRootCount: 0,
  projectLifecycleOwnerCount: 0,
  leaveGuardOwnerCount: 0,
  workspaceOwnerCount: 0
} : {
  studioRootCount: Number(ledger?.studio?.mountCount ?? -1),
  projectLifecycleOwnerCount: Number(ledger?.projectLifecycle?.ownerCount ?? -1),
  leaveGuardOwnerCount: Number(ledger?.leaveGuard?.ownerCount ?? -1),
  workspaceOwnerCount: Number(ledger?.workspace?.workspaceOwnerCount ?? -1)
};
process.stdout.write(JSON.stringify({
  status: evidence.status,
  expectation: evidence.expectation,
  route: evidence.route ?? null,
  targetUrl: evidence.targetUrl ?? null,
  rootKind,
  owners,
  legacyProjection: legacy,
  missingAssets: missing,
  meaningfulConsoleErrorCount: Array.isArray(evidence.meaningfulConsoleErrors)
    ? evidence.meaningfulConsoleErrors.length : 0,
  pageErrorCount: Array.isArray(evidence.runtimeErrors?.pageErrors)
    ? evidence.runtimeErrors.pageErrors.length : 0,
  meaningfulRequestFailureCount: Array.isArray(evidence.meaningfulRequestFailures)
    ? evidence.meaningfulRequestFailures.length : 0
}));
'@

function Read-EvidenceSummary {
    param([string]$Path)

    $output = @(& $nodeExe -e $evidenceSummaryScript $Path)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not summarize WebView2 evidence: $Path"
    }
    return ([string]::Join([Environment]::NewLine, $output) | ConvertFrom-Json)
}

function Assert-RunContract {
    param(
        [object]$Cleanup,
        [object]$Summary,
        [string]$ExpectedProfile,
        [string]$ExpectedPageKind,
        [bool]$ExpectedStudioUiEnabled,
        [bool]$ExpectedWorkspaceEnabled,
        [int]$WebPort
    )

    if (-not [bool]$Cleanup.passed -or
        -not [bool]$Cleanup.startupLog.passed -or
        [int]$Cleanup.startupLog.recordCount -ne 1) {
        throw "WebView2 cleanup/startup evidence did not pass for $($Cleanup.runName)."
    }
    $startup = $Cleanup.startupLog.record
    if ([string]$startup.profile -ne $ExpectedProfile -or
        [string]$startup.pageKind -ne $ExpectedPageKind -or
        [string]$startup.sourceSha -ne $sourceSha -or
        -not [bool]$startup.configurationRequiresRestart) {
        throw "Startup decision drifted for $($Cleanup.runName): $($startup | ConvertTo-Json -Depth 8)"
    }
    if ([bool]$startup.flags.'Studio:StudioUiEnabled' -ne $ExpectedStudioUiEnabled -or
        [bool]$startup.flags.'Studio:WorkspaceCapabilityEnabled' -ne $ExpectedWorkspaceEnabled -or
        [bool]$startup.flags.'Studio2.Workspace' -ne $ExpectedWorkspaceEnabled) {
        throw "Startup flags drifted for $($Cleanup.runName): $($startup.flags | ConvertTo-Json -Depth 4)"
    }
    $flagNames = @($startup.flags.PSObject.Properties.Name)
    foreach ($canonicalName in @(
        'Studio:StudioUiEnabled',
        'Studio:WorkspaceCapabilityEnabled',
        'Studio2.Workspace')) {
        if ($canonicalName -notin $flagNames) {
            throw "Startup log omitted canonical flag '$canonicalName'."
        }
    }
    foreach ($mixedName in @('StudioUiEnabled', 'WorkspaceCapabilityEnabled', 'Studio.Workspace')) {
        if ($mixedName -in $flagNames) {
            throw "Startup log emitted mixed flag name '$mixedName'."
        }
    }

    $expectedInitialUri = switch ($ExpectedPageKind) {
        "Legacy" { "http://localhost:$WebPort/index.html" }
        "StudioUi" { "http://localhost:$WebPort/studio/index.html" }
        default { $null }
    }
    if (-not [string]::Equals(
        [string]$startup.initialPageUri,
        [string]$expectedInitialUri,
        [System.StringComparison]::Ordinal)) {
        throw "Initial URI drifted for $($Cleanup.runName): $($startup.initialPageUri)"
    }
    if ([string]::IsNullOrWhiteSpace([string]$startup.assetRoot)) {
        throw "Startup asset root was not recorded for $($Cleanup.runName)."
    }
    if ([string]$Summary.status -ne "pass" -or
        [int]$Summary.meaningfulConsoleErrorCount -ne 0 -or
        [int]$Summary.pageErrorCount -ne 0 -or
        [int]$Summary.meaningfulRequestFailureCount -ne 0) {
        throw "WebView2 scenario evidence failed for $($Cleanup.runName): $($Summary | ConvertTo-Json -Depth 8)"
    }

    if ($ExpectedPageKind -eq "Legacy") {
        if ([string]$Summary.rootKind -ne "legacy" -or
            [int]$Summary.owners.studioRootCount -ne 0 -or
            [string]$Summary.legacyProjection.studioReadyType -ne "undefined" -or
            [string]$Summary.legacyProjection.studioDiagnosticsType -ne "undefined") {
            throw "Legacy run mounted a Next owner or lost its canonical root."
        }
    } elseif ($ExpectedPageKind -eq "StudioUi") {
        if ([string]$Summary.rootKind -ne "studio-ui" -or
            [int]$Summary.owners.studioRootCount -ne 1 -or
            [int]$Summary.owners.projectLifecycleOwnerCount -ne 1 -or
            [int]$Summary.owners.leaveGuardOwnerCount -ne 1 -or
            [int]$Summary.owners.workspaceOwnerCount -ne 0) {
            throw "Next run did not retain the single product owner ledger."
        }
    } else {
        if ([string]$Summary.rootKind -ne "diagnostic" -or
            [int]$Summary.missingAssets.legacyNavigationCount -ne 0 -or
            [int]$Summary.missingAssets.legacyMainCount -ne 0 -or
            [string]$Summary.missingAssets.studioReadyType -ne "undefined") {
            throw "Missing-assets run silently mounted Legacy or StudioUI."
        }
    }
}

$runRecords = [System.Collections.Generic.List[object]]::new()
$nextPortOffset = 0
$desktopBuilt = [bool]$NoBuild

function Invoke-ProfileRun {
    param(
        [string]$Name,
        [string]$Expectation,
        [bool]$WorkspaceEnabled,
        [string]$StartupProfile,
        [string]$ExpectedProfile,
        [string]$ExpectedPageKind,
        [string]$Route,
        [string]$ExecutablePath,
        [string]$RuntimeKind = "debug"
    )

    $webPort = $BaseWebPort + $script:nextPortOffset
    $cdpPort = $BaseCdpPort + $script:nextPortOffset
    $script:nextPortOffset += 1
    $relativeEvidence = "$relativeEvidenceRoot/runs/$Name/evidence"
    $parameters = @{
        Expectation = $Expectation
        EvidencePhase = "f04"
        Configuration = "Debug"
        RuntimeKind = $RuntimeKind
        DesktopExecutablePath = $ExecutablePath
        NodeExecutablePath = $nodeExe
        RunName = $Name
        EvidenceDirectory = $relativeEvidence
        WebPort = $webPort
        CdpPort = $cdpPort
        Scale = 1.0
        AuthMode = "HARNESS_SEEDED_SESSION"
        WindowWidth = 1600
        WindowHeight = 1000
    }
    if ($WorkspaceEnabled) {
        $parameters["WorkspaceCapabilityEnabled"] = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($StartupProfile)) {
        $parameters["StartupProfile"] = $StartupProfile
    }
    if (-not [string]::IsNullOrWhiteSpace($Route)) {
        $parameters["Route"] = $Route
    }
    if ($script:desktopBuilt -or $RuntimeKind -ne "debug") {
        $parameters["NoBuild"] = $true
    }

    & $singleRun @parameters | Out-Host
    $script:desktopBuilt = $true

    $evidencePath = Join-Path $repoRoot "$relativeEvidence/studio-ui-webview2-$Name.json"
    $cleanupPath = Join-Path $repoRoot "$relativeEvidence/studio-ui-webview2-$Name-cleanup.json"
    $cleanup = Get-Content -LiteralPath $cleanupPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $summary = Read-EvidenceSummary -Path $evidencePath
    Assert-RunContract `
        -Cleanup $cleanup `
        -Summary $summary `
        -ExpectedProfile $ExpectedProfile `
        -ExpectedPageKind $ExpectedPageKind `
        -ExpectedStudioUiEnabled ($Expectation -ne "legacy") `
        -ExpectedWorkspaceEnabled $WorkspaceEnabled `
        -WebPort $webPort

    $runRecords.Add([pscustomobject]@{
        name = $Name
        expectation = $Expectation
        explicitProfile = if ([string]::IsNullOrWhiteSpace($StartupProfile)) { $null } else { $StartupProfile }
        resolvedProfile = $ExpectedProfile
        studioUiEnabled = $Expectation -ne "legacy"
        workspaceCapabilityEnabled = $WorkspaceEnabled
        pageKind = $ExpectedPageKind
        webPort = $webPort
        cdpPort = $cdpPort
        status = "PASS"
        startup = $cleanup.startupLog.record
        ownerLedger = $summary.owners
        rootKind = $summary.rootKind
        evidencePath = $evidencePath
        cleanupPath = $cleanupPath
    })
}

$profileError = $null
try {
    Invoke-ProfileRun -Name "named-legacy" -Expectation "legacy" -WorkspaceEnabled $false `
        -StartupProfile "LEGACY_DEFAULT" -ExpectedProfile "LEGACY_DEFAULT" `
        -ExpectedPageKind "Legacy" -Route "" -ExecutablePath $desktopExe
    Invoke-ProfileRun -Name "named-next-pilot" -Expectation "studio-product" -WorkspaceEnabled $true `
        -StartupProfile "NEXT_PILOT" -ExpectedProfile "NEXT_PILOT" `
        -ExpectedPageKind "StudioUi" -Route "/overview" -ExecutablePath $desktopExe
    Invoke-ProfileRun -Name "named-next-full" -Expectation "studio-product" -WorkspaceEnabled $true `
        -StartupProfile "NEXT_FULL_CANDIDATE" -ExpectedProfile "NEXT_FULL_CANDIDATE" `
        -ExpectedPageKind "StudioUi" -Route "/overview" -ExecutablePath $desktopExe

    Invoke-ProfileRun -Name "truth-00" -Expectation "legacy" -WorkspaceEnabled $false `
        -StartupProfile "" -ExpectedProfile "LEGACY_DEFAULT" `
        -ExpectedPageKind "Legacy" -Route "" -ExecutablePath $desktopExe
    Invoke-ProfileRun -Name "truth-01" -Expectation "legacy" -WorkspaceEnabled $true `
        -StartupProfile "" -ExpectedProfile "ISOLATED_TRUTH_TABLE" `
        -ExpectedPageKind "Legacy" -Route "" -ExecutablePath $desktopExe
    Invoke-ProfileRun -Name "truth-10" -Expectation "studio-product" -WorkspaceEnabled $false `
        -StartupProfile "" -ExpectedProfile "ISOLATED_TRUTH_TABLE" `
        -ExpectedPageKind "StudioUi" -Route "/overview" -ExecutablePath $desktopExe
    Invoke-ProfileRun -Name "truth-11" -Expectation "studio-product" -WorkspaceEnabled $true `
        -StartupProfile "" -ExpectedProfile "NEXT_FULL_CANDIDATE" `
        -ExpectedPageKind "StudioUi" -Route "/overview" -ExecutablePath $desktopExe

    if (-not (Test-Path -LiteralPath $desktopExe -PathType Leaf)) {
        throw "The Desktop executable was not found after profile build: $desktopExe"
    }
    New-Item -ItemType Directory -Force -Path $publishCheckRoot | Out-Null
    Copy-Item -LiteralPath (Split-Path -Parent $desktopExe) `
        -Destination $missingAssetsRuntime -Recurse
    $studioAssets = Join-Path $missingAssetsRuntime "wwwroot/studio"
    if (-not (Test-Path -LiteralPath $studioAssets -PathType Container)) {
        throw "Missing-assets sample did not start with a StudioUI asset root."
    }
    Remove-Item -LiteralPath $studioAssets -Recurse -Force
    if (-not (Test-Path -LiteralPath (Join-Path $missingAssetsRuntime "wwwroot/index.html") -PathType Leaf)) {
        throw "Missing-assets sample lost the Legacy entry before the no-fallback test."
    }
    Invoke-ProfileRun -Name "missing-assets" -Expectation "missing-assets" -WorkspaceEnabled $true `
        -StartupProfile "NEXT_FULL_CANDIDATE" -ExpectedProfile "NEXT_FULL_CANDIDATE" `
        -ExpectedPageKind "Diagnostic" -Route "" `
        -ExecutablePath (Join-Path $missingAssetsRuntime "ClearVision.Product.Desktop.exe") `
        -RuntimeKind "missing-assets"
} catch {
    $profileError = $_
} finally {
    if (-not $KeepMissingAssetsRuntime) {
        Remove-VerifiedTemporaryDirectory -Path $missingAssetsRuntime -AllowedRoot $publishCheckRoot
        Remove-VerifiedTemporaryDirectory -Path $publishCheckRoot -AllowedRoot $publishCheckBoundary
    }
}

$appSettingsPath = Join-Path $repoRoot (
    "ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json")
$appSettings = Get-Content -LiteralPath $appSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$formalDefaultsPassed = -not [bool]$appSettings.Studio.StudioUiEnabled -and
    -not [bool]$appSettings.Studio.WorkspaceCapabilityEnabled
$namedProfilesPassed = @($runRecords | Where-Object { $_.name -like 'named-*' }).Count -eq 3
$truthTablePassed = @($runRecords | Where-Object { $_.name -like 'truth-*' }).Count -eq 4
$missingAssetsPassed = @($runRecords | Where-Object { $_.name -eq 'missing-assets' }).Count -eq 1
$canonicalFlagNamesPassed = $runRecords.Count -eq 8
$doubleRootPassed = @($runRecords | Where-Object {
    ($_.rootKind -eq 'legacy' -and [int]$_.ownerLedger.studioRootCount -eq 0) -or
    ($_.rootKind -eq 'studio-ui' -and [int]$_.ownerLedger.studioRootCount -eq 1) -or
    ($_.rootKind -eq 'diagnostic' -and [int]$_.ownerLedger.studioRootCount -eq 0)
}).Count -eq $runRecords.Count
$manifestPassed = -not $profileError -and $formalDefaultsPassed -and
    $namedProfilesPassed -and $truthTablePassed -and $missingAssetsPassed -and
    $canonicalFlagNamesPassed -and $doubleRootPassed

$manifest = [pscustomobject]@{
    schemaVersion = 1
    evidenceKind = "F04_G5_STARTUP_PROFILES"
    sourceSha = $sourceSha
    runName = $RunName
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    status = if ($manifestPassed) { "PASS" } else { "FAIL" }
    error = if ($profileError) { $profileError.Exception.Message } else { $null }
    formalDefaults = [pscustomobject]@{
        studioUiEnabled = [bool]$appSettings.Studio.StudioUiEnabled
        workspaceCapabilityEnabled = [bool]$appSettings.Studio.WorkspaceCapabilityEnabled
        profile = "LEGACY_DEFAULT"
        passed = $formalDefaultsPassed
    }
    namedProfiles = [pscustomobject]@{
        expected = @("LEGACY_DEFAULT", "NEXT_PILOT", "NEXT_FULL_CANDIDATE")
        passed = $namedProfilesPassed
    }
    startupTruthTable = [pscustomobject]@{
        combinations = 4
        independentProcesses = 4
        passed = $truthTablePassed
    }
    missingAssetDiagnostic = [pscustomobject]@{
        noSilentLegacyFallback = $missingAssetsPassed
        runtimeRetained = [bool]$KeepMissingAssetsRuntime
        passed = $missingAssetsPassed
    }
    configurationRequiresRestart = $true
    doubleRootGuard = [pscustomobject]@{
        passed = $doubleRootPassed
    }
    canonicalFlagNames = [pscustomobject]@{
        values = @(
            "Studio:StudioUiEnabled",
            "Studio:WorkspaceCapabilityEnabled",
            "Studio2.Workspace")
        passed = $canonicalFlagNamesPassed
    }
    runs = @($runRecords)
}
$manifestPath = Join-Path $evidenceRoot "studio-ui-profile-evidence.json"
[System.IO.File]::WriteAllText(
    $manifestPath,
    (($manifest | ConvertTo-Json -Depth 12) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

if ($profileError) {
    throw $profileError
}
if (-not $manifestPassed) {
    throw "StudioUI G5 profile evidence did not satisfy every gate: $manifestPath"
}

$manifest | ConvertTo-Json -Depth 8
