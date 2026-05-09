param(
    [string[]]$Roots = @(
        ".github",
        "Acme.OperatorLibrary/README.md",
        "Acme.OperatorLibrary/SBOM.md",
        "Acme.OperatorLibrary/THIRD-PARTY-NOTICES.md",
        "Acme.Product/src/Acme.Product.Application",
        "Acme.Product/src/Acme.Product.Desktop/wwwroot/index.html",
        "Acme.Product/src/Acme.Product.Desktop/wwwroot/login.html",
        "Acme.Product/src/Acme.Product.Desktop/Acme.Product.Desktop.csproj",
        "Acme.Product/src/Acme.Product.Desktop/wwwroot/src",
        "Acme.Product/src/Acme.Product.Core",
        "Acme.Product/src/Acme.Product.Infrastructure",
        "Acme.Product/src/Acme.Product.Desktop.Package",
        "Acme.Product/src/Acme.Product.Runtime",
        "Acme.Product/src/Acme.Product.Station",
        "docs/README.md",
        "docs/engineering",
        "docs/frontend",
        "docs/runtime",
        "models",
        "quality/evals/reports/operator_quality_matrix.md",
        "scripts",
        "tools",
        "README.md"
    ),

    [string[]]$Extensions = @(
        ".bat",
        ".cs",
        ".cshtml",
        ".css",
        ".html",
        ".js",
        ".json",
        ".md",
        ".mjs",
        ".ps1",
        ".props",
        ".targets",
        ".ts",
        ".txt",
        ".xml",
        ".yml",
        ".yaml"
    ),

    [switch]$IncludeArchives
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$utf8Strict = [System.Text.UTF8Encoding]::new($false, $true)

function New-UnicodeString {
    param([int[]]$CodePoints)

    return -join ($CodePoints | ForEach-Object { [char]$_ })
}

$mojibakeFragments = @(
    ([char]0xfffd),
    (New-UnicodeString @(0x59ab, 0x20ac)),
    (New-UnicodeString @(0x9366)),
    (New-UnicodeString @(0x6769)),
    (New-UnicodeString @(0x7ecb)),
    (New-UnicodeString @(0x9286)),
    (New-UnicodeString @(0x951b)),
    (New-UnicodeString @(0x95ab)),
    (New-UnicodeString @(0x93c8)),
    (New-UnicodeString @(0x93c6)),
    (New-UnicodeString @(0x5bb8)),
    (New-UnicodeString @(0x59dd)),
    (New-UnicodeString @(0x6e6a)),
    (New-UnicodeString @(0x935a)),
    (New-UnicodeString @(0x704f)),
    (New-UnicodeString @(0x7039)),
    (New-UnicodeString @(0x74a7, 0x52fe, 0x67a1)),
    (New-UnicodeString @(0x7ee0, 0x6940, 0x74f7)),
    (New-UnicodeString @(0x9429, 0xe3c5, 0x7d8d))
)

$excludedPathFragments = @(
    "\.git\",
    "\.vs\",
    "\.vscode\",
    "\.tmp\",
    "\.codex_tmp\",
    "\artifacts\",
    "\logs\",
    "\node_modules\",
    "\bin\",
    "\obj\",
    "\publish\",
    "\test_results\",
    "\build_err.txt",
    "\build_errors.txt",
    "\__pycache__\",
    ("\docs\" + (New-UnicodeString @(0x9762, 0x8bd5)) + "\image\"),
    "\scripts\OperatorDocGenerator\docs\",
    ("\scripts\OperatorDocGenerator\" + (New-UnicodeString @(0x7b97, 0x5b50, 0x8d44, 0x6599)) + "\")
)

if (-not $IncludeArchives) {
    $excludedPathFragments += @(
        ("\docs\" + (New-UnicodeString @(0x5f52, 0x6863)) + "\"),
        ("\docs\" + (New-UnicodeString @(0x5ba1, 0x8ba1, 0x8d44, 0x6599)) + "\" + (New-UnicodeString @(0x5916, 0x90e8, 0x5ba1, 0x8ba1)) + "\")
    )
}

function Resolve-CandidatePath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Test-ExcludedPath {
    param([string]$Path)

    $normalized = $Path.Replace("/", "\")
    foreach ($fragment in $excludedPathFragments) {
        if ($normalized.Contains($fragment)) {
            return $true
        }
    }

    return $false
}

function Get-RelativePath {
    param([string]$Path)

    $root = (Resolve-Path -LiteralPath $repoRoot).Path.TrimEnd("\", "/")
    $full = (Resolve-Path -LiteralPath $Path).Path
    if ($full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($root.Length).TrimStart("\", "/")
    }

    return $full
}

$files = New-Object System.Collections.Generic.List[string]
foreach ($root in $Roots) {
    $resolved = Resolve-CandidatePath -Path $root
    if (-not (Test-Path -LiteralPath $resolved)) {
        continue
    }

    $item = Get-Item -LiteralPath $resolved
    if (-not $item.PSIsContainer) {
        if ($Extensions -contains $item.Extension -and -not (Test-ExcludedPath -Path $item.FullName)) {
            $files.Add($item.FullName)
        }
        continue
    }

    Get-ChildItem -LiteralPath $item.FullName -Recurse -File | ForEach-Object {
        if ($Extensions -contains $_.Extension -and -not (Test-ExcludedPath -Path $_.FullName)) {
            $files.Add($_.FullName)
        }
    }
}

$failures = New-Object System.Collections.Generic.List[string]
foreach ($file in $files | Sort-Object -Unique) {
    $relative = Get-RelativePath -Path $file
    $bytes = [System.IO.File]::ReadAllBytes($file)

    try {
        $text = $utf8Strict.GetString($bytes)
    } catch {
        $failures.Add("$relative :: not valid UTF-8")
        continue
    }

    foreach ($fragment in $mojibakeFragments) {
        if ($text.Contains($fragment)) {
            $failures.Add("$relative :: contains mojibake fragment '$fragment'")
            break
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Error ("Text encoding scan failed:`n" + ($failures -join "`n"))
}

Write-Host "Text encoding scan passed for $($files.Count) files."
