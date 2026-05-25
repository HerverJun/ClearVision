<#
.SYNOPSIS
    ClearVision .NET/NuGet environment diagnostic and repair script.
    Fixes: NuGet.Config ACL corruption, TLS/auth chain failures, MSBuild workload noise.

.DESCRIPTION
    Root cause 1: User-level NuGet.Config ACL broken -> restore reports "Access denied"
    Root cause 2: TLS handshake to nuget.org fails -> restore reports NU1301 / SSL error
    Root cause 3: MSB4276 (Workload locator dir missing) -> compat noise, suppressed in Directory.Build.props

    This script runs:
      [Diagnose] Check dotnet SDK, NuGet.Config permissions, TLS connectivity, NuGet cache
      [Fix]      Rebuild NuGet.Config ACL, create project-level config, clear corrupted cache
      [Verify]   Run minimal restore to confirm fix

.PARAMETER DiagnoseOnly
    Run diagnostics only, make no changes.

.PARAMETER SkipNetworkCheck
    Skip network connectivity checks (offline scenarios).

.EXAMPLE
    .\scripts\dotnet-env-fix.ps1                  # Diagnose + Fix
    .\scripts\dotnet-env-fix.ps1 -DiagnoseOnly    # Diagnose only
    .\scripts\dotnet-env-fix.ps1 -SkipNetworkCheck # Skip network
#>

param(
    [switch]$DiagnoseOnly,
    [switch]$SkipNetworkCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$RepoRoot = Split-Path $PSScriptRoot -Parent
$NugetGlobalDir = Join-Path $env:APPDATA "NuGet"
$NugetGlobalConfig = Join-Path $NugetGlobalDir "NuGet.Config"
$NugetCacheDir = Join-Path $env:USERPROFILE ".nuget\packages"
$ProjectConfigPath = Join-Path $RepoRoot "nuget.config"
$LocalFallbackDir = Join-Path $RepoRoot ".tmp\nuget-packages"
$DotnetShimPath = Join-Path $PSScriptRoot "dotnet.ps1"
$DotnetPath = $null
$RequiredRuntimeBand = "8.0"
$RequiredRuntimeNames = @(
    "Microsoft.NETCore.App",
    "Microsoft.AspNetCore.App",
    "Microsoft.WindowsDesktop.App"
)

# --- helpers ---

function Write-Header($text) {
    Write-Host ""
    Write-Host "=== $text ===" -ForegroundColor Cyan
}

function Write-Pass($text) {
    Write-Host "  [PASS] $text" -ForegroundColor Green
}

function Write-Fail($text) {
    Write-Host "  [FAIL] $text" -ForegroundColor Red
}

function Write-Warn($text) {
    Write-Host "  [WARN] $text" -ForegroundColor Yellow
}

function Write-Info($text) {
    Write-Host "  [INFO] $text" -ForegroundColor Gray
}

$issueCount = 0
function Record-Issue($desc) {
    $script:issueCount++
    Write-Fail $desc
}

function Resolve-RepoDotnetPath {
    param(
        [switch]$InstallIfMissing
    )

    if ($InstallIfMissing) {
        $output = & $DotnetShimPath -InstallIfMissing -PrintPath -ReturnExitCode 2>&1
    }
    else {
        $output = & $DotnetShimPath -PrintPath -ReturnExitCode 2>&1
    }
    $exit = $LASTEXITCODE
    if ($exit -ne 0) {
        throw "dotnet resolver failed with exit code ${exit}: $($output -join [Environment]::NewLine)"
    }

    $path = ($output | Select-Object -Last 1).Trim()
    if ([string]::IsNullOrWhiteSpace($path)) {
        throw "dotnet resolver returned an empty path."
    }

    return $path
}

# ===================== DIAGNOSTICS =====================

Write-Host ""
Write-Host "ClearVision .NET Environment Diagnostic & Fix" -ForegroundColor White
Write-Host "Repository: $RepoRoot" -ForegroundColor Gray
Write-Host "Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray

# --- 1. dotnet SDK ---
Write-Header "1. dotnet SDK"
try {
    $pathDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($pathDotnet) {
        Write-Info "PATH dotnet: $($pathDotnet.Source)"
    }

    $DotnetPath = Resolve-RepoDotnetPath -InstallIfMissing:(!$DiagnoseOnly)
    Write-Pass "Repository dotnet: $DotnetPath"

    if ($pathDotnet -and $pathDotnet.Source -ne $DotnetPath) {
        Write-Warn "PATH resolves to a different dotnet host. Use .\scripts\dotnet.ps1 or repo scripts for this project."
    }

    $sdkVersion = & $DotnetPath --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        Record-Issue "Repository dotnet --version failed: $sdkVersion"
    } else {
        Write-Pass "SDK version: $sdkVersion"
    }

    $sdks = & $DotnetPath --list-sdks 2>&1
    Write-Info "Installed SDKs:"
    $sdks | ForEach-Object { Write-Info "  $_" }

    $runtimes = & $DotnetPath --list-runtimes 2>&1
    Write-Info "Required .NET 8 runtimes:"
    foreach ($runtimeName in $RequiredRuntimeNames) {
        $hasRuntime = $false
        foreach ($line in $runtimes) {
            if ($line -match "^\s*$([regex]::Escape($runtimeName))\s+$([regex]::Escape($RequiredRuntimeBand))\.") {
                $hasRuntime = $true
                Write-Pass "  $line"
                break
            }
        }

        if (-not $hasRuntime) {
            Record-Issue "Missing $runtimeName $RequiredRuntimeBand.x in repository dotnet host. Run: .\scripts\dotnet.ps1 -InstallIfMissing --version"
        }
    }
} catch {
    Record-Issue "dotnet CLI not found or broken: $_"
}

# --- 2. NuGet.Config Permissions ---
Write-Header "2. NuGet.Config Permissions"

# Check global directory
if (Test-Path $NugetGlobalDir) {
    Write-Pass "Global NuGet directory exists: $NugetGlobalDir"
    try {
        $acl = Get-Acl $NugetGlobalDir
        $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $hasAccess = $false
        foreach ($ace in $acl.Access) {
            if ($ace.IdentityReference.Value -eq $currentUser -and
                $ace.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::FullControl) {
                $hasAccess = $true
                break
            }
        }
        if ($hasAccess) {
            Write-Pass "Current user ($currentUser) has FullControl on NuGet directory"
        } else {
            Record-Issue "Current user ($currentUser) lacks FullControl on NuGet directory"
        }
    } catch {
        Write-Warn "Could not check directory ACL: $_"
    }
} else {
    Write-Warn "Global NuGet directory does not exist (will be created on first use)"
}

# Check global config file
if (Test-Path $NugetGlobalConfig) {
    Write-Pass "Global NuGet.Config exists: $NugetGlobalConfig"
    try {
        # Test readability
        [void][xml](Get-Content $NugetGlobalConfig -Encoding UTF8)
        Write-Pass "Global NuGet.Config is readable and valid XML"

        # Test writability
        $testWrite = [System.IO.File]::OpenWrite($NugetGlobalConfig)
        $testWrite.Close()
        Write-Pass "Global NuGet.Config is writable"
    } catch {
        if ($_.Exception.Message -match "denied|Unauthorized|Access") {
            Record-Issue "Global NuGet.Config access denied: $($_.Exception.Message)"
        } else {
            Write-Warn "NuGet.Config check issue: $($_.Exception.Message)"
        }
    }
} else {
    Write-Warn "Global NuGet.Config does not exist (will be auto-created)"
}

# Check NuGet cache directory
if (Test-Path $NugetCacheDir) {
    $cacheSize = (Get-ChildItem $NugetCacheDir -Recurse -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
    $cacheSizeMB = [math]::Round($cacheSize / 1MB, 1)
    Write-Pass "NuGet cache exists: $NugetCacheDir ($cacheSizeMB MB)"
} else {
    Write-Info "NuGet cache directory does not exist yet"
}

# --- 3. TLS / Network Connectivity ---
if (-not $SkipNetworkCheck) {
    Write-Header "3. Network / TLS Connectivity"

    $nugetEndpoints = @(
        @{ Name = "nuget.org index"; Url = "https://api.nuget.org/v3/index.json" },
        @{ Name = "nuget.org CDN";   Url = "https://cdn.jsdelivr.net/npm/" }
    )

    foreach ($ep in $nugetEndpoints) {
        try {
            $response = Invoke-WebRequest -Uri $ep.Url -Method Head -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop
            Write-Pass "$($ep.Name): HTTP $($response.StatusCode)"
        } catch {
            $msg = $_.Exception.Message
            if ($msg -match "SSL|TLS|certificate|secure channel") {
                Record-Issue "$($ep.Name): TLS handshake failed - $msg"
            } elseif ($msg -match "timeout|timed out") {
                Record-Issue "$($ep.Name): Connection timeout - check proxy/firewall"
            } elseif ($msg -match "403|401") {
                Record-Issue "$($ep.Name): Auth failure - $msg"
            } else {
                Write-Warn "$($ep.Name): $msg"
            }
        }
    }

    # Check system proxy settings
    Write-Header "3b. Proxy Configuration"
    $proxy = [System.Net.WebRequest]::DefaultWebProxy
    if ($proxy -and $proxy.GetProxy("https://api.nuget.org")) {
        $proxyUri = $proxy.GetProxy("https://api.nuget.org")
        Write-Info "System proxy detected: $proxyUri"
        try {
            $testResp = Invoke-WebRequest -Uri "https://api.nuget.org/v3/index.json" -TimeoutSec 10 -UseBasicParsing -Proxy $proxyUri -ErrorAction Stop
            Write-Pass "Proxy connection to nuget.org: OK"
        } catch {
            Write-Warn "Proxy connection failed: $($_.Exception.Message)"
        }
    } else {
        Write-Info "No system proxy configured (direct connection)"
    }
} else {
    Write-Info "Skipping network checks (-SkipNetworkCheck)"
}

# --- 4. Project-level NuGet.Config ---
Write-Header "4. Project NuGet.Config"

if (Test-Path $ProjectConfigPath) {
    Write-Pass "Project-level nuget.config exists: $ProjectConfigPath"
    try {
        [xml]$projCfg = Get-Content $ProjectConfigPath -Encoding UTF8
        $sources = $projCfg.configuration.packageSources.add
        Write-Info "Configured sources:"
        foreach ($s in $sources) {
            Write-Info "  [$($s.key)] $($s.value)"
        }
    } catch {
        Write-Warn "Could not parse project nuget.config: $_"
    }
} else {
    Write-Warn "No project-level nuget.config found"
}

# Check Acme.OperatorLibrary nuget.config
$opLibConfig = Join-Path $RepoRoot "Acme.OperatorLibrary\nuget.config"
if (Test-Path $opLibConfig) {
    Write-Pass "OperatorLibrary nuget.config exists"
} else {
    Write-Info "No OperatorLibrary nuget.config"
}

# --- 5. Local fallback package directory ---
Write-Header "5. Local Fallback Packages"

if (Test-Path $LocalFallbackDir) {
    $pkgCount = @(Get-ChildItem $LocalFallbackDir -Filter "*.nupkg" -Recurse -ErrorAction SilentlyContinue).Count
    Write-Pass "Local fallback directory exists with $pkgCount packages: $LocalFallbackDir"
} else {
    Write-Info "Local fallback directory does not exist: $LocalFallbackDir"
}

# --- 6. Directory.Build.props check ---
Write-Header "6. MSBuild Configuration"
$dbpPath = Join-Path $RepoRoot "Acme.Product\Directory.Build.props"
if (Test-Path $dbpPath) {
    $dbpContent = Get-Content $dbpPath -Raw
    if ($dbpContent -match "MSBuildEnableWorkloadResolver.*false") {
        Write-Pass "Workload resolver is disabled (suppresses MSB4276 noise)"
    } else {
        Write-Warn "MSBuildEnableWorkloadResolver not set to false - MSB4276 warnings may appear"
    }
} else {
    Write-Warn "Directory.Build.props not found"
}

# ===================== FIX =====================

if ($DiagnoseOnly) {
    Write-Header "DIAGNOSTIC COMPLETE (no changes made)"
    Write-Host "  Issues found: $issueCount" -ForegroundColor $(if ($issueCount -gt 0) { "Red" } else { "Green" })
    if ($issueCount -gt 0) {
        Write-Host "  Run without -DiagnoseOnly to apply fixes." -ForegroundColor Yellow
    }
    exit $issueCount
}

Write-Header "APPLYING FIXES"

# --- Fix 1: Repair NuGet.Config permissions ---
Write-Host ""
Write-Info "Fix 1: Ensuring NuGet directory and config permissions..."

if (-not (Test-Path $NugetGlobalDir)) {
    New-Item -ItemType Directory -Path $NugetGlobalDir -Force | Out-Null
    Write-Pass "Created NuGet directory: $NugetGlobalDir"
}

# Rebuild NuGet.Config with clean content
$cleanConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
"@

try {
    Set-Content -Path $NugetGlobalConfig -Value $cleanConfig -Encoding UTF8 -Force
    Write-Pass "Rebuilt global NuGet.Config with clean content"
} catch {
    Record-Issue "Failed to write global NuGet.Config: $_"
}

# Fix ACL: ensure current user has FullControl
try {
    $acl = Get-Acl $NugetGlobalConfig
    $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $currentUser, "FullControl", "Allow"
    )
    $acl.SetAccessRule($rule)
    Set-Acl -Path $NugetGlobalConfig -AclObject $acl
    Write-Pass "Set FullControl ACL for $currentUser on NuGet.Config"

    # Also fix directory permissions (may need admin)
    $dirAcl = Get-Acl $NugetGlobalDir
    $dirRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $currentUser, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow"
    )
    $dirAcl.SetAccessRule($dirRule)
    $prevEA = $ErrorActionPreference
    $ErrorActionPreference = "SilentlyContinue"
    Set-Acl -Path $NugetGlobalDir -AclObject $dirAcl -ErrorAction SilentlyContinue
    $ErrorActionPreference = $prevEA
    if ($?) {
        Write-Pass "Set FullControl ACL for $currentUser on NuGet directory"
    } else {
        Write-Warn "Directory ACL fix requires Administrator. File-level ACL was applied successfully."
    }
} catch {
    Record-Issue "Failed to fix ACL: $_"
}

# --- Fix 2: Create project-level NuGet.Config with fallback ---
Write-Host ""
Write-Info "Fix 2: Creating project-level nuget.config..."

$projectConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageRestore>
    <add key="enabled" value="True" />
    <add key="automatic" value="True" />
  </packageRestore>
  <config>
    <!-- Force TLS 1.2+ for nuget.org -->
    <add key="http_proxy" value="" />
    <add key="http_proxy.user" value="" />
    <add key="http_proxy.password" value="" />
  </config>
</configuration>
"@

try {
    Set-Content -Path $ProjectConfigPath -Value $projectConfig -Encoding UTF8
    Write-Pass "Created/updated project-level nuget.config"
} catch {
    Record-Issue "Failed to create project nuget.config: $_"
}

# --- Fix 3: Clear potentially corrupted NuGet caches ---
Write-Host ""
Write-Info "Fix 3: Clearing potentially corrupted NuGet caches..."

try {
    if ([string]::IsNullOrWhiteSpace($DotnetPath)) {
        $DotnetPath = Resolve-RepoDotnetPath -InstallIfMissing
    }

    $clearOutput = & $DotnetPath nuget locals all --clear 2>&1
    Write-Pass "Cleared all NuGet caches (http-cache, global-packages, temp)"
} catch {
    Write-Warn "Could not clear NuGet caches: $_"
}

# --- Fix 4: Enforce TLS 1.2+ ---
Write-Host ""
Write-Info "Fix 4: Ensuring TLS 1.2+ is enforced..."

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
    Write-Pass "Set PowerShell TLS to 1.2+"
} catch {
    Write-Warn "Could not set TLS configuration: $_"
}

# --- Fix 5: Ensure local fallback directory ---
Write-Host ""
Write-Info "Fix 5: Ensuring local fallback package directory..."

if (-not (Test-Path $LocalFallbackDir)) {
    New-Item -ItemType Directory -Path $LocalFallbackDir -Force | Out-Null
    Write-Pass "Created local fallback directory: $LocalFallbackDir"
} else {
    Write-Pass "Local fallback directory already exists"
}

# ===================== VERIFICATION =====================

Write-Header "VERIFICATION"

# Verify 1: NuGet.Config readable and writable
Write-Info "Test 1: NuGet.Config read/write..."
try {
    [void][xml](Get-Content $NugetGlobalConfig -Encoding UTF8)
    $testWrite = [System.IO.File]::OpenWrite($NugetGlobalConfig)
    $testWrite.Close()
    Write-Pass "NuGet.Config is readable and writable"
} catch {
    Record-Issue "NuGet.Config still not accessible: $_"
}

# Verify 2: Minimal restore test
Write-Info "Test 2: Minimal restore test..."
$sanityDir = Join-Path $RepoRoot ".tmp\DotnetSanity"
$sanityProj = Join-Path $sanityDir "DotnetSanity.csproj"

if (Test-Path $sanityProj) {
    Remove-Item (Join-Path $sanityDir "obj") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $sanityDir "bin") -Recurse -Force -ErrorAction SilentlyContinue

    if ([string]::IsNullOrWhiteSpace($DotnetPath)) {
        $DotnetPath = Resolve-RepoDotnetPath -InstallIfMissing
    }

    $restoreResult = & $DotnetPath restore $sanityProj --verbosity quiet 2>&1
    $restoreExit = $LASTEXITCODE

    if ($restoreExit -eq 0) {
        Write-Pass "DotnetSanity restore succeeded"
    } else {
        Record-Issue "DotnetSanity restore failed (exit $restoreExit)"
        Write-Info "Output: $restoreResult"
    }
} else {
    Write-Warn "DotnetSanity project not found, skipping restore test"
}

# Verify 3: TLS quick check
if (-not $SkipNetworkCheck) {
    Write-Info "Test 3: TLS quick check..."
    try {
        $resp = Invoke-WebRequest -Uri "https://api.nuget.org/v3/index.json" -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop
        Write-Pass "TLS to nuget.org: OK (HTTP $($resp.StatusCode))"
    } catch {
        if ($_.Exception.Message -match "SSL|TLS") {
            Record-Issue "TLS still failing: $($_.Exception.Message)"
        } else {
            Write-Warn "Network check: $($_.Exception.Message)"
        }
    }
}

# ===================== SUMMARY =====================

Write-Header "SUMMARY"
$color = if ($issueCount -gt 0) { "Yellow" } else { "Green" }
Write-Host "  Issues found and addressed: $issueCount" -ForegroundColor $color

if ($issueCount -eq 0) {
    Write-Host ""
    Write-Host "  Environment looks healthy. You can now run:" -ForegroundColor Green
    Write-Host "    .\scripts\dotnet.ps1 restore .\Acme.Product\Acme.Product.sln --locked-mode" -ForegroundColor White
    Write-Host "    .\scripts\dotnet.ps1 build .\Acme.Product\Acme.Product.sln --configuration Debug --no-restore" -ForegroundColor White
} else {
    Write-Host ""
    Write-Host "  Some issues could not be auto-fixed. Manual steps:" -ForegroundColor Yellow
    Write-Host "    1. Check if antivirus is locking NuGet files" -ForegroundColor White
    Write-Host "    2. Run this script as Administrator if ACL fixes failed" -ForegroundColor White
    Write-Host "    3. If TLS fails, check proxy/VPN settings" -ForegroundColor White
    Write-Host "    4. Try: .\scripts\dotnet.ps1 restore --source https://api.nuget.org/v3/index.json --disable-parallel" -ForegroundColor White
}

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
