[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$PublishDirectory,

    [string[]]$AllowedManifestRelativePath = @()
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
    throw "Publish directory does not exist: $PublishDirectory"
}

$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$allowedManifests = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($relativePath in $AllowedManifestRelativePath) {
    if (-not [string]::IsNullOrWhiteSpace($relativePath)) {
        $allowedManifests.Add($relativePath.Replace("\\", "/").TrimStart("/")) | Out-Null
    }
}

function Get-RelativePublishPath {
    param([string]$FullName)

    return [System.IO.Path]::GetRelativePath($publishRoot, $FullName).Replace("\\", "/")
}

function Test-NodePackageManifest {
    param([System.IO.FileInfo]$File)

    if ($File.Name -ine "package.json") {
        return $false
    }

    try {
        $manifest = Get-Content -LiteralPath $File.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        return $true
    }

    return $null -ne $manifest.scripts -or
        $null -ne $manifest.dependencies -or
        $null -ne $manifest.devDependencies -or
        $null -ne $manifest.peerDependencies -or
        $null -ne $manifest.optionalDependencies -or
        $null -ne $manifest.workspaces -or
        $null -ne $manifest.packageManager
}

$violations = [System.Collections.Generic.List[string]]::new()
$items = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -Force)
foreach ($item in $items) {
    $relativePath = Get-RelativePublishPath -FullName $item.FullName
    $segments = @($relativePath.Split("/", [System.StringSplitOptions]::RemoveEmptyEntries))
    if ($segments -contains "node_modules") {
        $violations.Add("node_modules: $relativePath") | Out-Null
        continue
    }

    if ($item.PSIsContainer) {
        continue
    }

    $name = $item.Name
    if ($name -match '(?i)(^|[._-])patch([._-]|$).*\.(ps1|bat|cmd)$') {
        $violations.Add("development patch script: $relativePath") | Out-Null
        continue
    }

    if ($name -match '(?i)^(node|nodejs|npm|npx)(\.exe|\.cmd|\.bat|\.ps1|\.dll|\.lib)?$') {
        $violations.Add("Node runtime/tooling: $relativePath") | Out-Null
        continue
    }

    if ($name -in @("package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml", ".npmrc")) {
        $violations.Add("development package manifest: $relativePath") | Out-Null
        continue
    }

    if (Test-NodePackageManifest -File $item) {
        $violations.Add("development package manifest: $relativePath") | Out-Null
        continue
    }

    if ($name -match '(?i)(^|[._-])manifest([._-]|$).*\.json$' -and -not $allowedManifests.Contains($relativePath)) {
        $violations.Add("non-release manifest: $relativePath") | Out-Null
    }
}

if ($violations.Count -gt 0) {
    $details = $violations | Sort-Object -Unique | ForEach-Object { " - $_" }
    throw "Release publish hygiene failed:`n$($details -join [Environment]::NewLine)"
}

Write-Host "Release publish hygiene passed: directory=$publishRoot files=$(@($items | Where-Object { -not $_.PSIsContainer }).Count)"
