[CmdletBinding()]
param(
    [string]$NodeExecutablePath,
    [string]$DesktopExecutablePath,
    [string]$RunName,
    [string]$EvidenceDirectory,
    [int]$BaseWebPort = 5700,
    [int]$BaseCdpPort = 9823,
    [ValidateSet("NEXT_DEFAULT_CANDIDATE", "NEXT_DEFAULT")]
    [string]$ExpectedConfiguredProfile = "NEXT_DEFAULT_CANDIDATE",
    [switch]$NoBuild,
    [switch]$KeepMissingAssetsRuntime
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($NoBuild) {
    throw "F09 profile evidence requires a fresh candidate build; -NoBuild is not supported."
}
if (-not [string]::IsNullOrWhiteSpace($DesktopExecutablePath)) {
    throw "F09 profile evidence must use the freshly built canonical Desktop executable."
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot "../.."))
$singleRun = Join-Path $scriptRoot "Invoke-StudioUiWebView2Evidence.ps1"

function Assert-CleanEvidenceWorktree {
    $changes = @(& git -C $repoRoot status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect the Git worktree before profile evidence."
    }
    if ($changes.Count -ne 0) {
        throw "Profile evidence requires a clean committed worktree; commit or remove local changes first."
    }
}

Assert-CleanEvidenceWorktree

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

function Get-JsonProperty {
    param(
        [object]$Object,
        [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Assert-OperatorPilotAuthorityEvidence {
    param([string]$Path)

    $evidence = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string](Get-JsonProperty -Object $evidence -Name "status") -ne "pass" -or
        [string](Get-JsonProperty -Object $evidence -Name "evidenceKind") -ne "F09_NEXT_OPERATOR_PILOT_REAL_AUTHORITY" -or
        [string](Get-JsonProperty -Object $evidence -Name "credentialSource") -ne
            "RUNNER_ISSUED_ADMIN_TOKEN_THEN_REAL_OPERATOR_LOGIN") {
        throw "Operator pilot evidence does not identify a real server-authority scenario: $Path"
    }

    $admin = Get-JsonProperty -Object $evidence -Name "admin"
    $operator = Get-JsonProperty -Object $evidence -Name "operator"
    if ([string](Get-JsonProperty -Object $admin -Name "role") -ne "Admin" -or
        [string]::IsNullOrWhiteSpace([string](Get-JsonProperty -Object $admin -Name "userId")) -or
        [string](Get-JsonProperty -Object $operator -Name "role") -ne "Operator" -or
        [string]::IsNullOrWhiteSpace([string](Get-JsonProperty -Object $operator -Name "userId")) -or
        [string]::IsNullOrWhiteSpace([string](Get-JsonProperty -Object $operator -Name "username"))) {
        throw "Operator pilot evidence is missing verified Admin or Operator identity."
    }

    $browserSession = Get-JsonProperty -Object $evidence -Name "browserSession"
    $browserUser = Get-JsonProperty -Object $browserSession -Name "user"
    if ([int](Get-JsonProperty -Object $browserSession -Name "status") -ne 200 -or
        [string](Get-JsonProperty -Object $browserUser -Name "role") -ne "Operator" -or
        [string](Get-JsonProperty -Object $browserUser -Name "username") -ne
            [string](Get-JsonProperty -Object $operator -Name "username")) {
        throw "Operator pilot browser session is not backed by the verified Operator identity."
    }

    $adminRejection = Get-JsonProperty -Object $evidence -Name "profileRejectsAdmin"
    if ([string](Get-JsonProperty -Object $adminRejection -Name "requestedRoute") -ne "/overview" -or
        [string](Get-JsonProperty -Object $adminRejection -Name "settledRoute") -ne "#/forbidden") {
        throw "Operator pilot did not prove that the profile rejects an Admin session."
    }

    $expectedReadOnlyRoutes = @("/overview", "/projects", "/operators", "/results", "/stations", "/about")
    $readOnlyRoutes = @((Get-JsonProperty -Object $evidence -Name "readOnlyRoutes") | Where-Object { $null -ne $_ })
    if ($readOnlyRoutes.Count -ne $expectedReadOnlyRoutes.Count) {
        throw "Operator pilot read-only route count drifted."
    }
    foreach ($route in $expectedReadOnlyRoutes) {
        $match = @($readOnlyRoutes | Where-Object {
            [string](Get-JsonProperty -Object $_ -Name "route") -eq $route -and
            [string](Get-JsonProperty -Object $_ -Name "hash") -eq ("#" + $route) -and
            [string](Get-JsonProperty -Object $_ -Name "pageState") -eq "ready" -and
            [int](Get-JsonProperty -Object (Get-JsonProperty -Object $_ -Name "authenticatedRead") -Name "status") -eq 200 -and
            [string](Get-JsonProperty -Object (Get-JsonProperty -Object $_ -Name "authenticatedRead") -Name "role") -eq "Operator"
        })
        if ($match.Count -ne 1) {
            throw "Operator pilot did not prove the read-only route '$route'."
        }
    }

    $expectedForbiddenRoutes = @(
        "/projects/00000000-0000-0000-0000-000000000000/workspace",
        "/ai",
        "/inspection",
        "/settings",
        "/diagnostics")
    $forbiddenRoutes = @((Get-JsonProperty -Object $evidence -Name "forbiddenRoutes") | Where-Object { $null -ne $_ })
    if ($forbiddenRoutes.Count -ne $expectedForbiddenRoutes.Count) {
        throw "Operator pilot forbidden route count drifted."
    }
    foreach ($route in $expectedForbiddenRoutes) {
        $match = @($forbiddenRoutes | Where-Object {
            [string](Get-JsonProperty -Object $_ -Name "requestedRoute") -eq $route -and
            [string](Get-JsonProperty -Object $_ -Name "settledRoute") -eq "#/forbidden"
        })
        if ($match.Count -ne 1) {
            throw "Operator pilot did not prove the forbidden route '$route'."
        }
    }

    $expectedDenials = @{
        "project-create" = "ProjectEditPermissionRequired"
        "formal-admission" = "HardwareOperationPermissionRequired"
        "formal-execute" = "HardwareOperationPermissionRequired"
        "plc-test-connection" = "HardwareOperationPermissionRequired"
    }
    $permissionDenials = @((Get-JsonProperty -Object $evidence -Name "permissionDenials") | Where-Object { $null -ne $_ })
    if ($permissionDenials.Count -ne $expectedDenials.Count) {
        throw "Operator pilot permission-denial count drifted."
    }
    foreach ($name in $expectedDenials.Keys) {
        $match = @($permissionDenials | Where-Object {
            [string](Get-JsonProperty -Object $_ -Name "name") -eq $name
        })
        if ($match.Count -ne 1 -or
            [int](Get-JsonProperty -Object $match[0] -Name "status") -ne 403 -or
            [string](Get-JsonProperty -Object $match[0] -Name "code") -ne $expectedDenials[$name]) {
            throw "Operator pilot did not prove the expected 403 denial '$name'."
        }
    }

    $startup = Get-JsonProperty -Object $evidence -Name "startupProjection"
    $featureFlags = Get-JsonProperty -Object $startup -Name "featureFlags"
    $allowedRoles = @((Get-JsonProperty -Object $startup -Name "allowedRoles") | Where-Object { $null -ne $_ })
    if ([string](Get-JsonProperty -Object $startup -Name "profile") -ne "NEXT_OPERATOR_PILOT" -or
        $allowedRoles.Count -ne 1 -or
        [string]$allowedRoles[0] -ne "Operator") {
        throw "Operator pilot startup projection drifted."
    }
    foreach ($flagName in @("Studio2.Workspace", "Studio2.Settings", "Studio2.InspectionRun", "Studio2.AiWorkbench")) {
        $flag = $featureFlags.PSObject.Properties[$flagName]
        if ($null -eq $flag -or [bool]$flag.Value) {
            throw "Operator pilot feature flag '$flagName' was not disabled."
        }
    }
    $stationsReadFlag = $featureFlags.PSObject.Properties["Studio2.StationsRead"]
    if ($null -eq $stationsReadFlag -or -not [bool]$stationsReadFlag.Value) {
        throw "Operator pilot did not retain the enabled Studio2.StationsRead projection."
    }

    return [pscustomobject]@{
        passed = $true
        evidenceKind = "F09_NEXT_OPERATOR_PILOT_REAL_AUTHORITY"
        credentialSource = "RUNNER_ISSUED_ADMIN_TOKEN_THEN_REAL_OPERATOR_LOGIN"
        adminRole = "Admin"
        operatorRole = "Operator"
        readOnlyRouteCount = $readOnlyRoutes.Count
        forbiddenRouteCount = $forbiddenRoutes.Count
        permissionDenialCount = $permissionDenials.Count
        browserSessionBackedByRealOperator = $true
    }
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
$desktopBuilt = $false
$profileDefinitions = @{
    "LEGACY_DEFAULT" = [pscustomobject]@{ StudioUiEnabled = $false; WorkspaceEnabled = $false }
    "LEGACY_FALLBACK" = [pscustomobject]@{ StudioUiEnabled = $false; WorkspaceEnabled = $false }
    "NEXT_INTERNAL_PILOT" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $true }
    "NEXT_ENGINEER_PILOT" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $true }
    "NEXT_OPERATOR_PILOT" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $false }
    "NEXT_DEFAULT_CANDIDATE" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $true }
    "NEXT_DEFAULT" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $true }
}
$operatorPilotScenario = Join-Path $repoRoot (
    "ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f09-operator-pilot.cjs")

function Invoke-ProfileRun {
    param(
        [string]$Name,
        [string]$Expectation,
        [string]$StartupProfile,
        [string]$ExpectedProfile,
        [string]$ExpectedPageKind,
        [string]$Route,
        [string]$ExecutablePath,
        [string]$RuntimeKind = "debug",
        [string]$NodeScenarioPath,
        [switch]$DeferAuthToScenario,
        [switch]$RequireOperatorPilotAuthority
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
    if (-not [string]::IsNullOrWhiteSpace($NodeScenarioPath)) {
        $parameters["NodeScenarioPath"] = $NodeScenarioPath
    }
    if ($DeferAuthToScenario) {
        $parameters["DeferAuthToScenario"] = $true
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

    $operatorPilotAuthority = $null
    if ($RequireOperatorPilotAuthority) {
        $operatorPilotAuthority = Assert-OperatorPilotAuthorityEvidence -Path $evidencePath
    }

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
        blocker = $null
        nodeScenarioPath = if ([string]::IsNullOrWhiteSpace($NodeScenarioPath)) { $null } else { $NodeScenarioPath }
        authenticationDeferredToScenario = [bool]$DeferAuthToScenario
        operatorPilotAuthority = $operatorPilotAuthority
        startup = $cleanup.startupLog.record
        ownerLedger = $summary.owners
        rootKind = $summary.rootKind
        evidencePath = $evidencePath
        cleanupPath = $cleanupPath
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
    if (-not (Test-Path -LiteralPath $operatorPilotScenario -PathType Leaf)) {
        throw "The F09 Operator pilot scenario was not found: $operatorPilotScenario"
    }
    Invoke-ProfileRun -Name "next-operator-pilot" -Expectation "studio-product" `
        -StartupProfile "NEXT_OPERATOR_PILOT" -ExpectedProfile "NEXT_OPERATOR_PILOT" `
        -ExpectedPageKind "StudioUi" -Route "/overview" -ExecutablePath $desktopExe `
        -NodeScenarioPath $operatorPilotScenario -RequireOperatorPilotAuthority
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
$configuredProfilePassed = [string]$appSettings.Studio.StartupProfile -eq $ExpectedConfiguredProfile -and
    [bool]$appSettings.Studio.StudioUiEnabled -and
    [bool]$appSettings.Studio.WorkspaceCapabilityEnabled
$operatorPilotRecord = @($runRecords | Where-Object { $_.name -eq 'next-operator-pilot' } | Select-Object -First 1)
$operatorReadOnlyAuthorityPassed = $operatorPilotRecord.Count -eq 1 -and
    $operatorPilotRecord[0].status -eq 'PASS' -and
    $null -ne $operatorPilotRecord[0].operatorPilotAuthority -and
    [bool]$operatorPilotRecord[0].operatorPilotAuthority.passed
$namedProfilesPassed = @($runRecords | Where-Object {
    $_.name -ne 'missing-assets' -and $_.status -eq 'PASS'
}).Count -eq $profileDefinitions.Count
$missingAssetsPassed = @($runRecords | Where-Object {
    $_.name -eq 'missing-assets' -and $_.status -eq 'PASS'
}).Count -eq 1
$executedRuns = @($runRecords | Where-Object { $_.status -eq 'PASS' })
$canonicalFlagNamesPassed = $executedRuns.Count -eq ($profileDefinitions.Count + 1)
$doubleRootPassed = @($executedRuns | Where-Object {
    ($_.rootKind -eq 'legacy' -and [int]$_.ownerLedger.studioRootCount -eq 0) -or
    ($_.rootKind -eq 'studio-ui' -and [int]$_.ownerLedger.studioRootCount -eq 1) -or
    ($_.rootKind -eq 'diagnostic' -and [int]$_.ownerLedger.studioRootCount -eq 0)
}).Count -eq $executedRuns.Count
$manifestPassed = -not $profileError -and $configuredProfilePassed -and
    $namedProfilesPassed -and $operatorReadOnlyAuthorityPassed -and $missingAssetsPassed -and
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
        expectedConfiguredProfile = $ExpectedConfiguredProfile
        nextDefaultActive = [string]$appSettings.Studio.StartupProfile -eq "NEXT_DEFAULT"
        configuredProfilePassed = $configuredProfilePassed
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
        evidenceScope = "G3_READ_ONLY_PROFILE_AND_SERVER_AUTHORITY"
        status = if ($operatorPilotRecord.Count -eq 1) { $operatorPilotRecord[0].status } else { "NOT_RECORDED" }
        scenarioPath = if ($operatorPilotRecord.Count -eq 1) { $operatorPilotRecord[0].nodeScenarioPath } else { $null }
        authorityChecks = if ($operatorPilotRecord.Count -eq 1) { $operatorPilotRecord[0].operatorPilotAuthority } else { $null }
        realServerAuthority = $operatorReadOnlyAuthorityPassed
        readOnlyProfileContractPassed = $operatorReadOnlyAuthorityPassed
    }
    g6CutoverGate = [pscustomobject]@{
        operatorPilot = [pscustomobject]@{
            status = "BLOCKED"
            minimumPilotPassed = $false
            defaultEntrySwitchAllowed = $false
            reason = (
                "The existing backend contract correctly rejects Operator formal admission, formal execution, " +
                "continuous inspection, and PLC control. This G3 scenario proves a real Operator's read-only " +
                "profile and denial boundaries only; it does not prove the G6 approved operation, result, and " +
                "exit/restart workflow required before default entry can switch.")
        }
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
