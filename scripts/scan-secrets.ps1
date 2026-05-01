[CmdletBinding()]
param(
    [string]$Path = "."
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = (Resolve-Path -LiteralPath $Path).Path
$maxFileBytes = 5MB

$excludedDirectories = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    ".git",
    ".tmp",
    ".vs",
    "artifacts",
    "bin",
    "coverage",
    "node_modules",
    "nupkg",
    "obj",
    "playwright-report",
    "publish",
    "test-results",
    "TestResults",
    "test_results"
) | ForEach-Object { [void]$excludedDirectories.Add($_) }

$excludedExtensions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    ".7z",
    ".bmp",
    ".coverage",
    ".db",
    ".dll",
    ".exe",
    ".gif",
    ".ico",
    ".jpeg",
    ".jpg",
    ".nupkg",
    ".pdb",
    ".png",
    ".snupkg",
    ".sqlite",
    ".webp",
    ".zip"
) | ForEach-Object { [void]$excludedExtensions.Add($_) }

$rules = [ordered]@{
    "OpenAI/compatible sk token" = 'sk-[A-Za-z0-9][A-Za-z0-9_-]{20,}'
    "GitHub classic token"      = 'ghp_[A-Za-z0-9_]{20,}'
    "GitHub fine-grained token" = 'github_pat_[A-Za-z0-9_]{20,}'
    "AWS access key id"         = 'AKIA[0-9A-Z]{16}'
    "Google API key"            = 'AIza[0-9A-Za-z\-_]{35}'
    "Slack token"               = 'xox[baprs]-[A-Za-z0-9-]{10,}'
    "JWT"                       = 'eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}'
    "Private key block"         = '-----BEGIN (RSA |DSA |EC |OPENSSH |)?PRIVATE KEY-----'
}

function Get-RelativePath {
    param(
        [string]$BasePath,
        [string]$TargetPath
    )

    $resolvedBase = [System.IO.Path]::GetFullPath($BasePath)
    if (-not $resolvedBase.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $resolvedBase += [System.IO.Path]::DirectorySeparatorChar
    }

    $resolvedTarget = [System.IO.Path]::GetFullPath($TargetPath)
    $baseUri = [System.Uri]::new($resolvedBase)
    $targetUri = [System.Uri]::new($resolvedTarget)

    return [System.Uri]::UnescapeDataString(
        $baseUri.MakeRelativeUri($targetUri).ToString()
    ).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function Test-ExcludedFile {
    param([System.IO.FileInfo]$File)

    if ($File.Length -gt $maxFileBytes) {
        return $true
    }

    if ($excludedExtensions.Contains($File.Extension)) {
        return $true
    }

    $relativePath = Get-RelativePath -BasePath $root -TargetPath $File.FullName
    foreach ($segment in ($relativePath -split '[\\/]')) {
        if ($excludedDirectories.Contains($segment)) {
            return $true
        }
    }

    return $false
}

function Get-TextLines {
    param([System.IO.FileInfo]$File)

    try {
        return [System.IO.File]::ReadLines(
            $File.FullName,
            [System.Text.UTF8Encoding]::new($false, $true))
    }
    catch {
        return $null
    }
}

$findings = [System.Collections.Generic.List[object]]::new()
$files = Get-ChildItem -LiteralPath $root -File -Recurse -Force

foreach ($file in $files) {
    if (Test-ExcludedFile -File $file) {
        continue
    }

    $lines = Get-TextLines -File $file
    if ($null -eq $lines) {
        continue
    }

    $lineNumber = 0
    foreach ($line in $lines) {
        $lineNumber++
        if ($null -eq $line) {
            continue
        }

        if ($line.Length -gt 12000 -and $line -match '^\s*"(image/[^"]+|application/octet-stream)"\s*:') {
            continue
        }

        if ($line.Contains("<REDACTED>")) {
            continue
        }

        foreach ($rule in $rules.GetEnumerator()) {
            if ($line -match $rule.Value) {
                $relativePath = Get-RelativePath -BasePath $root -TargetPath $file.FullName
                $findings.Add([pscustomobject]@{
                    Path = $relativePath
                    Line = $lineNumber
                    Rule = $rule.Key
                })
            }
        }
    }
}

if ($findings.Count -gt 0) {
    Write-Host "Secret scan failed. Potential secret locations:"
    foreach ($finding in $findings) {
        Write-Host ("{0}:{1}: {2}" -f $finding.Path, $finding.Line, $finding.Rule)
    }
    Write-Host "Full secret values are intentionally not printed."
    throw "Secret scan failed with $($findings.Count) potential secret(s)."
}

Write-Host "Secret scan passed: no high-confidence secret patterns found."
