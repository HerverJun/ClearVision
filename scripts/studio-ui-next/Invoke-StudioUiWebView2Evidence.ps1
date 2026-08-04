[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        "legacy",
        "studio-diagnostics",
        "studio-product",
        "studio-auth",
        "studio-design",
        "studio-canvas",
        "missing-assets")]
    [string]$Expectation,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateSet("debug", "publish", "missing-assets")]
    [string]$RuntimeKind = "debug",
    [ValidateSet("f01", "f02", "f03", "f04", "f06", "f09")]
    [string]$EvidencePhase = "f01",
    [string]$DesktopExecutablePath,
    [string]$NodeExecutablePath,
    [string]$NodeScenarioPath,
    [string]$RunName,
    [string]$Route,
    [string]$EvidenceDirectory,
    [string]$RuntimeDirectory,
    [int]$WebPort = 5100,
    [int]$CdpPort = 9423,
    [double]$Scale = 1.0,
    [int]$WindowWidth = 1600,
    [int]$WindowHeight = 1000,
    [switch]$SanitizeDesktopPath,
    [switch]$DeepCanvas,
    [switch]$SeedWorkspace,
    [switch]$FormalRun,
    [switch]$GoldenJourney,
    [switch]$DpiOnly,
    [ValidateSet(
        "LEGACY_DEFAULT",
        "LEGACY_FALLBACK",
        "NEXT_INTERNAL_PILOT",
        "NEXT_ENGINEER_PILOT",
        "NEXT_OPERATOR_PILOT",
        "NEXT_DEFAULT_CANDIDATE",
        "NEXT_DEFAULT",
        "NEXT_PILOT",
        "NEXT_FULL_CANDIDATE")]
    [string]$StartupProfile,
    [string]$AuthMode = "HARNESS_SEEDED_SESSION",
    [string]$RollbackPhase,
    [string]$RollbackStatePath,
    [string]$FinalJourneyPhase,
    [string]$FinalJourneyStatePath,
    [int]$SoakCycles = 0,
    [string]$DatabasePath,
    [string]$Username = "admin",
    [string]$Password,
    [switch]$KeepDatabase,
    [switch]$ReuseDatabase,
    [switch]$AllowInitialAdminSetup,
    [switch]$DeferAuthToScenario,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot "../.."))
$sharedRunner = Join-Path $repoRoot "scripts/run-ai-webview2-release-smoke.ps1"
$uiTests = Join-Path $repoRoot "ClearVision.Product/tests/ClearVision.Product.UI.Tests"
$defaultScenario = Join-Path $uiTests "tests/e2e/studio-ui-next/studio-ui-webview2-smoke.cjs"
$scenario = if ([string]::IsNullOrWhiteSpace($NodeScenarioPath)) {
    $defaultScenario
} else {
    [System.IO.Path]::GetFullPath($NodeScenarioPath)
}
$nodeExe = if ([string]::IsNullOrWhiteSpace($NodeExecutablePath)) {
    (Get-Command node.exe -ErrorAction Stop).Source
} else {
    [System.IO.Path]::GetFullPath($NodeExecutablePath)
}
$desktopExe = if ([string]::IsNullOrWhiteSpace($DesktopExecutablePath)) {
    Join-Path $repoRoot (
        "ClearVision.Product/src/ClearVision.Product.Desktop/bin/" +
        "$Configuration/net8.0-windows/win-x64/ClearVision.Product.Desktop.exe")
} else {
    [System.IO.Path]::GetFullPath($DesktopExecutablePath)
}
$sourceSha = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceSha)) {
    throw "Could not resolve the source SHA for WebView2 evidence."
}
if ($sourceSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw "The WebView2 evidence source SHA is not a 40-character commit SHA."
}

function ConvertTo-SafeRunName {
    param([string]$Value)

    $safe = $Value -replace '[^A-Za-z0-9_.-]+', '-'
    $safe = $safe.Trim('-')
    if ([string]::IsNullOrWhiteSpace($safe)) {
        throw "RunName must contain at least one safe filename character."
    }
    return $safe
}

if ([string]::IsNullOrWhiteSpace($RunName)) {
    $RunName = "{0}-{1}" -f $Expectation, [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss-fff")
}
$RunName = ConvertTo-SafeRunName -Value $RunName

if ($WebPort -lt 1 -or $WebPort -gt 65535) {
    throw "WebPort must be between 1 and 65535."
}
if ($CdpPort -lt 1 -or $CdpPort -gt 65535) {
    throw "CdpPort must be between 1 and 65535."
}
if ($WebPort -eq $CdpPort) {
    throw "WebPort and CdpPort must be different."
}
if ($Scale -le 0) {
    throw "Scale must be greater than zero."
}
if (-not (Test-Path -LiteralPath $sharedRunner -PathType Leaf)) {
    throw "The shared WebView2 runner was not found: $sharedRunner"
}
if (-not (Test-Path -LiteralPath $scenario -PathType Leaf)) {
    throw "The WebView2 Node scenario was not found: $scenario"
}
if (-not (Test-Path -LiteralPath $nodeExe -PathType Leaf)) {
    throw "The absolute Node driver was not found: $nodeExe"
}

$defaultEvidenceDirectory = ".tmp/studio-ui-next/$EvidencePhase/$RunName/evidence"
$relativeEvidence = if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $defaultEvidenceDirectory
} else {
    $EvidenceDirectory.Replace('\', '/')
}
if ([System.IO.Path]::IsPathRooted($relativeEvidence)) {
    throw "EvidenceDirectory must be repository-relative because the shared runner resolves it from repoRoot."
}

$evidencePath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $relativeEvidence))
$allowedEvidenceRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ".tmp/studio-ui-next"))
$allowedEvidencePrefix = $allowedEvidenceRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $evidencePath.StartsWith(
    $allowedEvidencePrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "EvidenceDirectory must remain under .tmp/studio-ui-next."
}
$cleanupPath = Join-Path $evidencePath "studio-ui-webview2-$RunName-cleanup.json"
if ($cleanupPath.Length -gt 240) {
    throw (
        "The cleanup evidence path is too long for Windows PowerShell compatibility " +
        "($($cleanupPath.Length) characters; maximum 240). Use a shorter RunName or EvidenceDirectory.")
}
if (Test-Path -LiteralPath $evidencePath) {
    throw "Evidence directory already exists; use a unique RunName: $evidencePath"
}

$runRoot = Split-Path -Parent $evidencePath
$runtimeRoot = if ([string]::IsNullOrWhiteSpace($RuntimeDirectory)) {
    Join-Path $runRoot "runtime"
} else {
    [System.IO.Path]::GetFullPath($RuntimeDirectory)
}
$runtimeParent = [System.IO.Path]::GetFullPath((Split-Path -Parent $runtimeRoot))
$runtimeVolumeRoot = [System.IO.Path]::GetPathRoot($runtimeParent)
if ([string]::Equals(
    $runtimeParent.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar),
    $runtimeVolumeRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar),
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "RuntimeDirectory must be nested below a dedicated temporary parent."
}
if (Test-Path -LiteralPath $runtimeRoot) {
    throw "RuntimeDirectory already exists; use an isolated path: $runtimeRoot"
}
$runtimePrefix = $runtimeParent.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $runtimeRoot.StartsWith(
    $runtimePrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "RuntimeDirectory must be a child of its dedicated temporary parent."
}
$hostLogs = Join-Path $runRoot "host-logs"
$webView2UserData = Join-Path $runtimeRoot "webview2"
$conversationStore = Join-Path $runtimeRoot "conversation"
$agentRunStore = Join-Path $runtimeRoot "agent-runs"
$configuredDatabasePath = $DatabasePath
$databasePath = if ([string]::IsNullOrWhiteSpace($configuredDatabasePath)) {
    Join-Path $runtimeRoot "database/vision.db"
} else {
    [System.IO.Path]::GetFullPath($configuredDatabasePath)
}
New-Item -ItemType Directory -Force -Path $evidencePath | Out-Null

$startupProfileDefinitions = @{
    "LEGACY_DEFAULT" = [pscustomobject]@{ StudioUiEnabled = $false; WorkspaceEnabled = $false }
    "LEGACY_FALLBACK" = [pscustomobject]@{ StudioUiEnabled = $false; WorkspaceEnabled = $false }
    "NEXT_INTERNAL_PILOT" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $true }
    "NEXT_ENGINEER_PILOT" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $true }
    "NEXT_OPERATOR_PILOT" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $false }
    "NEXT_DEFAULT_CANDIDATE" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $true }
    "NEXT_DEFAULT" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $true }
    "NEXT_PILOT" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $true }
    "NEXT_FULL_CANDIDATE" = [pscustomobject]@{ StudioUiEnabled = $true; WorkspaceEnabled = $true }
}
if ([string]::IsNullOrWhiteSpace($StartupProfile)) {
    $StartupProfile = if ($Expectation -eq "legacy") {
        "LEGACY_DEFAULT"
    } else {
        "NEXT_DEFAULT_CANDIDATE"
    }
}
$StartupProfile = $StartupProfile.Trim().ToUpperInvariant()
if (-not $startupProfileDefinitions.ContainsKey($StartupProfile)) {
    throw "Unsupported StartupProfile '$StartupProfile'."
}
$startupProfileDefinition = $startupProfileDefinitions[$StartupProfile]
$studioUiEnabled = [bool]$startupProfileDefinition.StudioUiEnabled
$workspaceCapabilityEnabled = [bool]$startupProfileDefinition.WorkspaceEnabled
if ($Expectation -eq "legacy" -and $studioUiEnabled) {
    throw "StartupProfile '$StartupProfile' cannot be combined with a Legacy expectation."
}
if ($Expectation -ne "legacy" -and -not $studioUiEnabled) {
    throw "StartupProfile '$StartupProfile' cannot be combined with a StudioUI expectation."
}
if ($SeedWorkspace -and $Expectation -ne "studio-product") {
    throw "SeedWorkspace is only valid for the studio-product expectation."
}
if ($SeedWorkspace -and -not $workspaceCapabilityEnabled) {
    throw "SeedWorkspace requires a StartupProfile with Workspace enabled."
}
if ($FormalRun -and -not $SeedWorkspace) {
    throw "FormalRun requires SeedWorkspace so the runner can execute a persisted Project authority."
}
if ($GoldenJourney -and ($Expectation -ne "studio-product" -or -not $SeedWorkspace -or -not $FormalRun)) {
    throw "GoldenJourney requires studio-product plus SeedWorkspace and FormalRun."
}
if ($DpiOnly -and ($Expectation -ne "studio-product" -or -not $SeedWorkspace)) {
    throw "DpiOnly requires studio-product plus SeedWorkspace."
}
if ($DpiOnly -and $FormalRun) {
    throw "DpiOnly and FormalRun are separate evidence scopes and cannot share one run."
}
if (($KeepDatabase -or $ReuseDatabase) -and [string]::IsNullOrWhiteSpace($configuredDatabasePath)) {
    throw "KeepDatabase/ReuseDatabase require an explicit isolated DatabasePath."
}
if ($AllowInitialAdminSetup -and
    ([string]::IsNullOrWhiteSpace($configuredDatabasePath) -or
        [string]::IsNullOrWhiteSpace($Password))) {
    throw "AllowInitialAdminSetup requires an explicit isolated DatabasePath and Password."
}
if ([string]::IsNullOrWhiteSpace($AuthMode)) {
    throw "AuthMode must be non-empty."
}
$validRollbackPhases = @("", "NEXT_CREATE", "LEGACY_VERIFY", "NEXT_REOPEN")
$normalizedRollbackPhase = ([string]$RollbackPhase).Trim().ToUpperInvariant()
if ($normalizedRollbackPhase -notin $validRollbackPhases) {
    throw "Unsupported RollbackPhase '$RollbackPhase'."
}
if ($normalizedRollbackPhase -and [string]::IsNullOrWhiteSpace($RollbackStatePath)) {
    throw "RollbackPhase requires RollbackStatePath."
}
$rollbackStateFullPath = if ([string]::IsNullOrWhiteSpace($RollbackStatePath)) {
    ""
} elseif ([System.IO.Path]::IsPathRooted($RollbackStatePath)) {
    [System.IO.Path]::GetFullPath($RollbackStatePath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $RollbackStatePath))
}
$validFinalJourneyPhases = @("", "CREATE_RUN_LOGOUT", "REOPEN_DELETE", "SOAK")
$normalizedFinalJourneyPhase = ([string]$FinalJourneyPhase).Trim().ToUpperInvariant()
if ($normalizedFinalJourneyPhase -notin $validFinalJourneyPhases) {
    throw "Unsupported FinalJourneyPhase '$FinalJourneyPhase'."
}
if ($normalizedFinalJourneyPhase -and -not $DeferAuthToScenario) {
    throw "FinalJourneyPhase requires DeferAuthToScenario so setup/login remain UI-owned."
}
if ($normalizedFinalJourneyPhase -and
    ($Expectation -ne "studio-product" -or -not $workspaceCapabilityEnabled -or
        $SeedWorkspace -or $FormalRun -or $DpiOnly -or $normalizedRollbackPhase)) {
    throw "FinalJourneyPhase requires an unseeded studio-product NEXT profile with Workspace enabled."
}
if ($normalizedFinalJourneyPhase -and
    ([string]::IsNullOrWhiteSpace($configuredDatabasePath) -or
        [string]::IsNullOrWhiteSpace($Password))) {
    throw "FinalJourneyPhase requires an explicit isolated DatabasePath and UI password."
}
if ($normalizedFinalJourneyPhase -eq "SOAK") {
    if ($SoakCycles -lt 20) {
        throw "SOAK requires at least 20 cycles."
    }
} elseif ($SoakCycles -ne 0) {
    throw "SoakCycles is only valid with FinalJourneyPhase SOAK."
}
$finalJourneyStateFullPath = if ([string]::IsNullOrWhiteSpace($FinalJourneyStatePath)) {
    ""
} elseif ([System.IO.Path]::IsPathRooted($FinalJourneyStatePath)) {
    [System.IO.Path]::GetFullPath($FinalJourneyStatePath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $FinalJourneyStatePath))
}
if ($normalizedFinalJourneyPhase -in @("CREATE_RUN_LOGOUT", "REOPEN_DELETE") -and
    [string]::IsNullOrWhiteSpace($finalJourneyStateFullPath)) {
    throw "$normalizedFinalJourneyPhase requires FinalJourneyStatePath."
}
$databaseAllowedPrefix = $allowedEvidenceRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not [string]::IsNullOrWhiteSpace($configuredDatabasePath) -and
    -not $databasePath.StartsWith($databaseAllowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "DatabasePath must remain under .tmp/studio-ui-next."
}
$runtimeRootPrefix = $runtimeRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if ($KeepDatabase -and
    $databasePath.StartsWith($runtimeRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "A retained DatabasePath must be outside the per-run RuntimeDirectory."
}
if ($rollbackStateFullPath -and
    -not $rollbackStateFullPath.StartsWith(
        $databaseAllowedPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "RollbackStatePath must remain under .tmp/studio-ui-next."
}
if ($finalJourneyStateFullPath -and
    -not $finalJourneyStateFullPath.StartsWith(
        $databaseAllowedPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "FinalJourneyStatePath must remain under .tmp/studio-ui-next."
}
$resolvedRoute = if (-not [string]::IsNullOrWhiteSpace($Route)) {
    $Route
} elseif ($SeedWorkspace) {
    "/projects/seeded/workspace"
} elseif ($Expectation -eq "studio-product") {
    "/overview"
} elseif ($Expectation -eq "studio-auth") {
    "/login"
} elseif ($Expectation -eq "studio-design") {
    "/labs/design"
} elseif ($Expectation -eq "studio-canvas") {
    "/labs/canvas"
} else {
    "/diagnostics"
}

$customEnvironment = [ordered]@{
    "Studio__StartupProfile" = $StartupProfile
    "CV_STUDIO_UI_EXPECTATION" = $Expectation
    "CV_STUDIO_UI_ROUTE" = $resolvedRoute
    "CV_STUDIO_UI_DESKTOP_EXECUTABLE" = $desktopExe
    "CV_STUDIO_UI_RUN_NAME" = $RunName
    "CV_STUDIO_UI_RUNTIME_KIND" = $RuntimeKind
    "CV_STUDIO_UI_CONFIGURATION" = $Configuration
    "CV_STUDIO_UI_EVIDENCE_PHASE" = $EvidencePhase
    "CV_STUDIO_UI_SOURCE_SHA" = $sourceSha
    "CV_STUDIO_UI_SANITIZED_PATH" = if ($SanitizeDesktopPath) { "true" } else { "false" }
    "CV_STUDIO_UI_DEEP_CANVAS" = if ($DeepCanvas) { "true" } else { "false" }
    "CV_STUDIO_UI_SEED_WORKSPACE" = if ($SeedWorkspace) { "true" } else { "false" }
    "CV_STUDIO_UI_FORMAL_RUN" = if ($FormalRun) { "true" } else { "false" }
    "CV_STUDIO_UI_G4B_GOLDEN_JOURNEY" = if ($GoldenJourney) { "true" } else { "false" }
    "CV_STUDIO_UI_DPI_ONLY" = if ($DpiOnly) { "true" } else { "false" }
    "CV_STUDIO_UI_AUTH_MODE" = $AuthMode.Trim().ToUpperInvariant()
    "CV_STUDIO_UI_ROLLBACK_PHASE" = $normalizedRollbackPhase
    "CV_STUDIO_UI_ROLLBACK_STATE" = $rollbackStateFullPath
    "CV_STUDIO_UI_FINAL_JOURNEY_PHASE" = $normalizedFinalJourneyPhase
    "CV_STUDIO_UI_FINAL_JOURNEY_STATE" = $finalJourneyStateFullPath
    "CV_STUDIO_UI_SOAK_CYCLES" = [string]$SoakCycles
    "CV_NATIVE_DPI_PROBE" = Join-Path $scriptRoot "Get-DesktopRuntimeProbe.ps1"
}
$previousEnvironment = @{}
foreach ($entry in $customEnvironment.GetEnumerator()) {
    $previousEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process")
}
$profileRawOptionEnvironmentNames = @(
    "Studio__StudioUiEnabled",
    "Studio__WorkspaceCapabilityEnabled",
    "Studio__NodePreviewInspectorEnabled",
    "Studio__PropertyPanelCapabilityEnabled",
    "Studio__PreviewPanelCapabilityEnabled",
    "Studio__GlobalVariablesCapabilityEnabled",
    "Studio__SettingsCapabilityEnabled",
    "Studio__ProjectPageCapabilityEnabled",
    "Studio__InspectionCapabilityEnabled",
    "Studio__StationsReadCapabilityEnabled",
    "Studio__InspectionRunCapabilityEnabled",
    "Studio__ResultsReviewCapabilityEnabled",
    "Studio__AiPanelCapabilityEnabled",
    "Studio__AiWorkbenchCapabilityEnabled",
    "Studio__CircleSearchV2ToolEnabled",
    "Studio__NPointCalibrationWorkbenchEnabled"
)
$profileRawOptionPreviousEnvironment = @{}
foreach ($name in $profileRawOptionEnvironmentNames) {
    $profileRawOptionPreviousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}
$runnerManagedEnvironmentNames = @(
    "Database__Path",
    "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
    "CV_DESKTOP_HTTP_PORT",
    "CV_WEBVIEW2_USER_DATA_FOLDER",
    "CV_CONVERSATION_STORE_ROOT",
    "CV_AGENT_RUN_EVENT_STORE",
    "CV_DESKTOP_LOG_PATH",
    "CV_CDP_PORT",
    "CV_WEB_PORT",
    "CV_DPI_SCALE",
    "CV_SMOKE_PHASE",
    "CV_SMOKE_TOKEN",
    "CV_SMOKE_USER",
    "CV_SMOKE_USERNAME",
    "CV_SMOKE_PASSWORD",
    "CV_EVIDENCE_DIR",
    "PATH"
)
$runnerPreviousEnvironment = @{}
foreach ($name in $runnerManagedEnvironmentNames) {
    $runnerPreviousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

function Restore-CustomEnvironment {
    foreach ($entry in $customEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable(
            $entry.Key,
            $previousEnvironment[$entry.Key],
            "Process")
    }
    foreach ($name in $profileRawOptionEnvironmentNames) {
        [Environment]::SetEnvironmentVariable(
            $name,
            $profileRawOptionPreviousEnvironment[$name],
            "Process")
    }
}

function Test-EnvironmentRestored {
    foreach ($entry in $customEnvironment.GetEnumerator()) {
        $current = [Environment]::GetEnvironmentVariable($entry.Key, "Process")
        $expected = $previousEnvironment[$entry.Key]
        if (-not [string]::Equals(
            [string]$current,
            [string]$expected,
            [System.StringComparison]::Ordinal)) {
            return $false
        }
    }
    foreach ($name in $runnerManagedEnvironmentNames) {
        $current = [Environment]::GetEnvironmentVariable($name, "Process")
        $expected = $runnerPreviousEnvironment[$name]
        if (-not [string]::Equals(
            [string]$current,
            [string]$expected,
            [System.StringComparison]::Ordinal)) {
            return $false
        }
    }
    foreach ($name in $profileRawOptionEnvironmentNames) {
        $current = [Environment]::GetEnvironmentVariable($name, "Process")
        $expected = $profileRawOptionPreviousEnvironment[$name]
        if (-not [string]::Equals(
            [string]$current,
            [string]$expected,
            [System.StringComparison]::Ordinal)) {
            return $false
        }
    }
    return $true
}

function Get-MatchingDesktopProcesses {
    $expected = [System.IO.Path]::GetFullPath($desktopExe)
    return @(Get-CimInstance Win32_Process -Filter "Name = 'ClearVision.Product.Desktop.exe'" |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
            [string]::Equals(
                [System.IO.Path]::GetFullPath($_.ExecutablePath),
                $expected,
                [System.StringComparison]::OrdinalIgnoreCase)
        } |
        ForEach-Object {
            [pscustomobject]@{
                processId = [int]$_.ProcessId
                parentProcessId = [int]$_.ParentProcessId
                executablePath = [string]$_.ExecutablePath
                commandLine = [string]$_.CommandLine
            }
        })
}

function Test-TcpPortAvailable {
    param([int]$Port)

    $listener = $null
    try {
        $listener = [System.Net.Sockets.TcpListener]::new(
            [System.Net.IPAddress]::Loopback,
            $Port)
        $listener.Start()
        return $true
    } catch {
        return $false
    } finally {
        if ($listener) {
            $listener.Stop()
        }
    }
}

$runnerParameters = @{
    Configuration = $Configuration
    EvidenceDirectory = $relativeEvidence
    DesktopExecutablePath = $desktopExe
    NodeSmokePath = $scenario
    NodeExecutablePath = $nodeExe
    WebPort = $WebPort
    CdpPort = $CdpPort
    Scale = $Scale
    Phase = $EvidencePhase
    RunName = $RunName
    HostLogDirectory = $hostLogs
    WebView2UserDataDirectory = $webView2UserData
    ConversationStoreRoot = $conversationStore
    AgentRunStoreRoot = $agentRunStore
    RuntimeCleanupRoot = $runtimeRoot
    DatabasePath = $databasePath
    WindowWidth = $WindowWidth
    WindowHeight = $WindowHeight
    SingleRun = $true
    Username = $Username
}
if (-not [string]::IsNullOrWhiteSpace($Password)) {
    $runnerParameters["Password"] = $Password
}
if ($KeepDatabase) {
    $runnerParameters["KeepDatabase"] = $true
}
if ($ReuseDatabase) {
    $runnerParameters["ReuseDatabase"] = $true
}
if ($AllowInitialAdminSetup) {
    $runnerParameters["AllowInitialAdminSetup"] = $true
}
if ($DeferAuthToScenario) {
    $runnerParameters["DeferAuthToScenario"] = $true
}
if ($SanitizeDesktopPath) {
    $runnerParameters["SanitizeDesktopPath"] = $true
}
if ($NoBuild) {
    $runnerParameters["NoBuild"] = $true
}

$runnerSucceeded = $false
$runnerError = $null
$startedAtUtc = [DateTime]::UtcNow

try {
    foreach ($name in $profileRawOptionEnvironmentNames) {
        [Environment]::SetEnvironmentVariable($name, $null, "Process")
    }
    foreach ($entry in $customEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process")
    }

    & $sharedRunner @runnerParameters
    $runnerSucceeded = $true
} catch {
    $runnerError = $_
} finally {
    Restore-CustomEnvironment
}

$databaseArtifacts = @(
    $databasePath,
    ($databasePath + "-shm"),
    ($databasePath + "-wal")
)
$matchingProcesses = @(Get-MatchingDesktopProcesses)
$webView2UserDataRemoved = -not (Test-Path -LiteralPath $webView2UserData)
$conversationStoreRemoved = -not (Test-Path -LiteralPath $conversationStore)
$agentRunStoreRemoved = -not (Test-Path -LiteralPath $agentRunStore)
$databaseArtifactsRemoved = -not ($databaseArtifacts | Where-Object {
    Test-Path -LiteralPath $_
})
$databaseStatePassed = if ($KeepDatabase) {
    Test-Path -LiteralPath $databasePath -PathType Leaf
} else {
    $databaseArtifactsRemoved
}
$databaseInsideRuntime = $databasePath.StartsWith(
    $runtimeRootPrefix,
    [System.StringComparison]::OrdinalIgnoreCase)
$runtimeRootRemovalError = $null
if ($webView2UserDataRemoved -and
    $conversationStoreRemoved -and
    $agentRunStoreRemoved -and
    (-not $databaseInsideRuntime -or $databaseArtifactsRemoved) -and
    (Test-Path -LiteralPath $runtimeRoot)) {
    try {
        Remove-Item -LiteralPath $runtimeRoot -Recurse -Force
    } catch {
        $runtimeRootRemovalError = $_.Exception.Message
    }
}
$runtimeRootRemoved = -not (Test-Path -LiteralPath $runtimeRoot)
$webPortReleased = Test-TcpPortAvailable -Port $WebPort
$cdpPortReleased = Test-TcpPortAvailable -Port $CdpPort
$startupRecords = [System.Collections.Generic.List[object]]::new()
foreach ($logFile in Get-ChildItem -LiteralPath $hostLogs -Recurse -File -Filter "*-desktop*.log" -ErrorAction SilentlyContinue) {
    foreach ($line in Get-Content -LiteralPath $logFile.FullName -Encoding UTF8) {
        $marker = "[StudioStartup] "
        $markerIndex = $line.IndexOf($marker, [System.StringComparison]::Ordinal)
        if ($markerIndex -lt 0) {
            continue
        }
        $json = $line.Substring($markerIndex + $marker.Length)
        try {
            $startupRecords.Add(($json | ConvertFrom-Json))
        } catch {
            $startupRecords.Add([pscustomobject]@{
                parseError = $_.Exception.Message
                raw = $json
            })
        }
    }
}
$startupRecord = if ($startupRecords.Count -eq 1) { $startupRecords[0] } else { $null }
$expectedProfile = $StartupProfile
$expectedPageKind = if ($Expectation -eq "legacy") {
    "Legacy"
} elseif ($Expectation -eq "missing-assets") {
    "Diagnostic"
} else {
    "StudioUi"
}
$startupRecordPassed = $startupRecord -and
    [string]$startupRecord.profile -eq $expectedProfile -and
    [string]$startupRecord.pageKind -eq $expectedPageKind -and
    [string]$startupRecord.sourceSha -eq $sourceSha.ToLowerInvariant() -and
    [string]$startupRecord.authMode -eq $AuthMode.Trim().ToUpperInvariant() -and
    -not [string]::IsNullOrWhiteSpace([string]$startupRecord.assetRoot) -and
    [bool]$startupRecord.configurationRequiresRestart -and
    [bool]$startupRecord.flags.'Studio:StudioUiEnabled' -eq $studioUiEnabled -and
    [bool]$startupRecord.flags.'Studio:WorkspaceCapabilityEnabled' -eq $workspaceCapabilityEnabled -and
    [bool]$startupRecord.flags.'Studio2.Workspace' -eq $workspaceCapabilityEnabled
$cleanup = [pscustomobject]@{
    schemaVersion = 1
    runName = $RunName
    expectation = $Expectation
    evidencePhase = $EvidencePhase
    startedAtUtc = $startedAtUtc.ToString("O")
    capturedAtUtc = [DateTime]::UtcNow.ToString("O")
    runnerSucceeded = $runnerSucceeded
    runnerError = if ($runnerError) { [string]$runnerError.Exception.Message } else { $null }
    studioUiEnabled = $studioUiEnabled
    workspaceCapabilityEnabled = $workspaceCapabilityEnabled
    workspaceSeededByHarness = [bool]$SeedWorkspace
    formalRun = [bool]$FormalRun
    dpiOnly = [bool]$DpiOnly
    startupProfileRequested = $StartupProfile
    startupProfileExpected = $expectedProfile
    authMode = $AuthMode.Trim().ToUpperInvariant()
    rollbackPhase = $normalizedRollbackPhase
    rollbackStatePath = if ($rollbackStateFullPath) { $rollbackStateFullPath } else { $null }
    finalJourneyPhase = if ($normalizedFinalJourneyPhase) { $normalizedFinalJourneyPhase } else { $null }
    finalJourneyStatePath = if ($finalJourneyStateFullPath) { $finalJourneyStateFullPath } else { $null }
    soakCycles = $SoakCycles
    authenticationDeferredToScenario = [bool]$DeferAuthToScenario
    sanitizedDesktopPath = [bool]$SanitizeDesktopPath
    externalNodeDriver = [pscustomobject]@{
        executablePath = $nodeExe
        isAbsolute = [System.IO.Path]::IsPathRooted($nodeExe)
        isInsideDesktopProcessTree = $false
    }
    retainedEvidence = Test-Path -LiteralPath $evidencePath -PathType Container
    retainedHostLogs = Test-Path -LiteralPath $hostLogs -PathType Container
    processCleanup = [pscustomobject]@{
        passed = $matchingProcesses.Count -eq 0
        remaining = $matchingProcesses
    }
    portCleanup = [pscustomobject]@{
        webPort = $WebPort
        webPortReleased = $webPortReleased
        cdpPort = $CdpPort
        cdpPortReleased = $cdpPortReleased
        passed = $webPortReleased -and $cdpPortReleased
    }
    runtimeCleanup = [pscustomobject]@{
        root = $runtimeRoot
        webView2UserDataRemoved = $webView2UserDataRemoved
        conversationStoreRemoved = $conversationStoreRemoved
        agentRunStoreRemoved = $agentRunStoreRemoved
        databasePath = $databasePath
        databaseKept = [bool]$KeepDatabase
        databaseReused = [bool]$ReuseDatabase
        databaseArtifactsRemoved = $databaseArtifactsRemoved
        databaseStatePassed = $databaseStatePassed
        runtimeRootRemoved = $runtimeRootRemoved
        removalError = $runtimeRootRemovalError
    }
    startupLog = [pscustomobject]@{
        recordCount = $startupRecords.Count
        record = $startupRecord
        passed = [bool]$startupRecordPassed
    }
    environmentRestored = Test-EnvironmentRestored
}
$cleanupPassed = $cleanup.processCleanup.passed -and
    $cleanup.portCleanup.passed -and
    $cleanup.runtimeCleanup.webView2UserDataRemoved -and
    $cleanup.runtimeCleanup.conversationStoreRemoved -and
    $cleanup.runtimeCleanup.agentRunStoreRemoved -and
    $cleanup.runtimeCleanup.databaseStatePassed -and
    $cleanup.runtimeCleanup.runtimeRootRemoved -and
    $cleanup.startupLog.passed -and
    $cleanup.environmentRestored
$cleanup | Add-Member -NotePropertyName passed -NotePropertyValue $cleanupPassed

New-Item -ItemType Directory -Force -Path $evidencePath | Out-Null
[System.IO.File]::WriteAllText(
    $cleanupPath,
    (($cleanup | ConvertTo-Json -Depth 8) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

if ($runnerError) {
    throw $runnerError
}
if (-not $cleanupPassed) {
    throw "WebView2 evidence run succeeded but cleanup verification failed. See $cleanupPath"
}

[pscustomobject]@{
    Succeeded = $true
    RunName = $RunName
    Expectation = $Expectation
    EvidencePhase = $EvidencePhase
    EvidenceDirectory = $evidencePath
    CleanupEvidence = $cleanupPath
    CompletedAtUtc = [DateTime]::UtcNow.ToString("O")
} | ConvertTo-Json -Depth 4
