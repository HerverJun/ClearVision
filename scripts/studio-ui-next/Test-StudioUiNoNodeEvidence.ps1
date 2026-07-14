[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,
    [Parameter(Mandatory = $true)]
    [string]$RuntimeEvidenceDirectory,
    [string]$OutputPath,
    [switch]$NoThrow
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$publishRoot = [System.IO.Path]::GetFullPath($PublishDirectory)
$runtimeRoot = [System.IO.Path]::GetFullPath($RuntimeEvidenceDirectory)
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
$staticPassed = $forbidden.Count -eq 0 -and
    @($requiredChecks.Values | Where-Object { $_ -ne $true }).Count -eq 0

$runtimeDocuments = [System.Collections.Generic.List[object]]::new()
foreach ($file in Get-ChildItem -LiteralPath $runtimeRoot -Recurse -File -Filter "*.json") {
    try {
        $document = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
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
        $document = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
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
            externalNodeDriver = $_.document.externalNodeDriver
            passed = $_.document.runnerSucceeded -eq $true -and $_.document.passed -eq $true
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
    $sanitizedPathPassed -and $externalDriverRecorded
$report = [pscustomobject]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    publishDirectory = $publishRoot
    runtimeEvidenceDirectory = $runtimeRoot
    publishStaticScan = [pscustomobject]@{
        status = if ($staticPassed) { "PASS" } else { "BLOCKED" }
        requiredPaths = $requiredChecks
        forbiddenArtifactCount = $forbidden.Count
        forbiddenArtifacts = @($forbidden)
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
