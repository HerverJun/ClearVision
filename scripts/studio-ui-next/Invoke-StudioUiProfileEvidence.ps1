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
    ".tmp/studio-ui-next/f09/profiles/$RunName"
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
    Join-Path $repoRoot ".tmp/publish-check/studio-ui-next-f09"))
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
$profileDefinitions = @{
    "LEGACY_DEFAULT" = [pscustomobject]@{ StudioUiEnabled = $false; WorkspaceEnabled = $false }
    "LEGACY_FALLBACK" = [pscustomobject]@{ StudioUiEnabled = $false; WorkspaceEnabled = $false }
    "NEXT_INTERNAL_PILOT" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $true }
    "NEXT_ENGINEER_PILOT" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $true }
    "NEXT_OPERATOR_PILOT" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $false }
    "NEXT_DEFAULT_CANDIDATE" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $true }
    "NEXT_DEFAULT" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $true }
}

function Invoke-ProfileRun {
    param(
        [string]$Name,
        [string]$Expectation,
        [string]$StartupProfile,
        [string]$ExpectedProfile,
        [string]$ExpectedPageKind,
        [string]$Route,
        [string]$ExecutablePath,
        [string]$RuntimeKind = "debug"
    )

    $profileDefinition = $profileDefinitions[$StartupProfile]
    if ($null -eq $profileDefinition) {
        throw "Profile evidence has no F09 definition for '$StartupProfile'."
    }

    $webPort = $BaseWebPort + $script:nextPortOffset
    $cdpPort = $BaseCdpPort + $script:nextPortOffset
    $script:nextPortOffset += 1
    $relativeEvidence = "$relativeEvidenceRoot/runs/$Name/evidence"
    $parameters = @{
        Expectation = $Expectation
        EvidencePhase = "f09"
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
    $parameters["StartupProfile"] = $StartupProfile
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
        -ExpectedStudioUiEnabled ([bool]$profileDefinition.StudioUiEnabled) `
        -ExpectedWorkspaceEnabled ([bool]$profileDefinition.WorkspaceEnabled) `
        -WebPort $webPort

    $runRecords.Add([pscustomobject]@{
        name = $Name
        expectation = $Expectation
        explicitProfile = $StartupProfile
        resolvedProfile = $ExpectedProfile
        studioUiEnabled = [bool]$profileDefinition.StudioUiEnabled
        workspaceCapabilityEnabled = [bool]$profileDefinition.WorkspaceEnabled
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

function Add-BlockedProfileRun {
    param(
        [string]$Name,
        [string]$StartupProfile,
        [string]$Reason
    )

    $profileDefinition = $profileDefinitions[$StartupProfile]
    if ($null -eq $profileDefinition) {
        throw "Profile evidence has no F09 definition for '$StartupProfile'."
    }
    $runRecords.Add([pscustomobject]@{
        name = $Name
        expectation = "studio-product"
        explicitProfile = $StartupProfile
        resolvedProfile = $StartupProfile
        studioUiEnabled = [bool]$profileDefinition.StudioUiEnabled
        workspaceCapabilityEnabled = [bool]$profileDefinition.WorkspaceEnabled
        pageKind = "StudioUi"
        status = "BLOCKED"
        blocker = $Reason
        startup = $null
        ownerLedger = $null
        rootKind = $null
        evidencePath = $null
        cleanupPath = $null
    })
}

$profileError = $null
try {
    Invoke-ProfileRun -Name "legacy-default" -Expectation "legacy" `
        -StartupProfile "LEGACY_DEFAULT" -ExpectedProfile "LEGACY_DEFAULT" `
        -ExpectedPageKind "Legacy" -Route "" -ExecutablePath $desktopExe
    Invoke-ProfileRun -Name "legacy-fallback" -Expectation "legacy" `
        -StartupProfile "LEGACY_FALLBACK" -ExpectedProfile "LEGACY_FALLBACK" `
        -ExpectedPageKind "Legacy" -Route "" -ExecutablePath $desktopExe
    Invoke-ProfileRun -Name "next-internal-pilot" -Expectation "studio-product" `
        -StartupProfile "NEXT_INTERNAL_PILOT" -ExpectedProfile "NEXT_INTERNAL_PILOT" `
        -ExpectedPageKind "StudioUi" -Route "/overview" -ExecutablePath $desktopExe
    Invoke-ProfileRun -Name "next-engineer-pilot" -Expectation "studio-product" `
        -StartupProfile "NEXT_ENGINEER_PILOT" -ExpectedProfile "NEXT_ENGINEER_PILOT" `
        -ExpectedPageKind "StudioUi" -Route "/overview" -ExecutablePath $desktopExe
    Add-BlockedProfileRun -Name "next-operator-pilot" `
        -StartupProfile "NEXT_OPERATOR_PILOT" `
        -Reason (
            "The isolated WebView2 harness can bootstrap only an initial Admin and " +
            "cannot prove the Operator formal-run or continuous-inspection permission contract. " +
            "This remains an F09 cutover blocker; no backend permission is widened for evidence.")
    Invoke-ProfileRun -Name "next-default-candidate" -Expectation "studio-product" `
        -StartupProfile "NEXT_DEFAULT_CANDIDATE" -ExpectedProfile "NEXT_DEFAULT_CANDIDATE" `
        -ExpectedPageKind "StudioUi" -Route "/overview" -ExecutablePath $desktopExe
    Invoke-ProfileRun -Name "next-default" -Expectation "studio-product" `
        -StartupProfile "NEXT_DEFAULT" -ExpectedProfile "NEXT_DEFAULT" `
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
    Invoke-ProfileRun -Name "missing-assets" -Expectation "missing-assets" `
        -StartupProfile "NEXT_DEFAULT_CANDIDATE" -ExpectedProfile "NEXT_DEFAULT_CANDIDATE" `
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
$candidateConfigurationPassed = [string]$appSettings.Studio.StartupProfile -eq "NEXT_DEFAULT_CANDIDATE" -and
    [bool]$appSettings.Studio.StudioUiEnabled -and
    [bool]$appSettings.Studio.WorkspaceCapabilityEnabled
$operatorPilotRecord = @($runRecords | Where-Object { $_.name -eq 'next-operator-pilot' } | Select-Object -First 1)
$operatorPilotPassed = $operatorPilotRecord.Count -eq 1 -and $operatorPilotRecord[0].status -eq 'PASS'
$namedProfilesPassed = @($runRecords | Where-Object {
    $_.name -ne 'missing-assets' -and $_.name -ne 'next-operator-pilot' -and $_.status -eq 'PASS'
}).Count -eq 6
$missingAssetsPassed = @($runRecords | Where-Object { $_.name -eq 'missing-assets' }).Count -eq 1
$executedRuns = @($runRecords | Where-Object { $_.status -eq 'PASS' })
$canonicalFlagNamesPassed = $executedRuns.Count -eq 7
$doubleRootPassed = @($executedRuns | Where-Object {
    ($_.rootKind -eq 'legacy' -and [int]$_.ownerLedger.studioRootCount -eq 0) -or
    ($_.rootKind -eq 'studio-ui' -and [int]$_.ownerLedger.studioRootCount -eq 1) -or
    ($_.rootKind -eq 'diagnostic' -and [int]$_.ownerLedger.studioRootCount -eq 0)
}).Count -eq $executedRuns.Count
$manifestPassed = -not $profileError -and $candidateConfigurationPassed -and
    $namedProfilesPassed -and $operatorPilotPassed -and $missingAssetsPassed -and
    $canonicalFlagNamesPassed -and $doubleRootPassed

$manifest = [pscustomobject]@{
    schemaVersion = 1
    evidenceKind = "F09_G3_STARTUP_PROFILES"
    sourceSha = $sourceSha
    runName = $RunName
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    status = if ($manifestPassed) { "PASS" } elseif ($profileError) { "FAIL" } else { "PARTIAL" }
    error = if ($profileError) { $profileError.Exception.Message } else { $null }
    defaultEntry = [pscustomobject]@{
        studioUiEnabled = [bool]$appSettings.Studio.StudioUiEnabled
        workspaceCapabilityEnabled = [bool]$appSettings.Studio.WorkspaceCapabilityEnabled
        configuredProfile = [string]$appSettings.Studio.StartupProfile
        nextDefaultActive = [string]$appSettings.Studio.StartupProfile -eq "NEXT_DEFAULT"
        candidateConfigurationPassed = $candidateConfigurationPassed
    }
    namedProfiles = [pscustomobject]@{
        expected = @(
            "LEGACY_DEFAULT",
            "LEGACY_FALLBACK",
            "NEXT_INTERNAL_PILOT",
            "NEXT_ENGINEER_PILOT",
            "NEXT_OPERATOR_PILOT",
            "NEXT_DEFAULT_CANDIDATE",
            "NEXT_DEFAULT")
        passed = $namedProfilesPassed
    }
    operatorPilot = [pscustomobject]@{
        status = if ($operatorPilotRecord.Count -eq 1) { $operatorPilotRecord[0].status } else { "NOT_RECORDED" }
        blocker = if ($operatorPilotRecord.Count -eq 1) { $operatorPilotRecord[0].blocker } else { $null }
        passed = $operatorPilotPassed
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
    throw "StudioUI F09 profile evidence is incomplete: $manifestPath"
}

$manifest | ConvertTo-Json -Depth 8
