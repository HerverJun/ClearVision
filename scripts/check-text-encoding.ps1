[CmdletBinding()]
param(
    [string[]]$Roots = @(
        ".github",
        "ClearVision.OperatorLibrary/README.md",
        "ClearVision.OperatorLibrary/SBOM.md",
        "ClearVision.OperatorLibrary/THIRD-PARTY-NOTICES.md",
        "ClearVision.Product/src/ClearVision.Product.Application",
        "ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/index.html",
        "ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/login.html",
        "ClearVision.Product/src/ClearVision.Product.Desktop/ClearVision.Product.Desktop.csproj",
        "ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src",
        "ClearVision.Product/src/ClearVision.Product.Core",
        "ClearVision.Product/src/ClearVision.Product.Infrastructure",
        "ClearVision.Product/src/ClearVision.Product.Desktop.Package",
        "ClearVision.Product/src/ClearVision.Product.Runtime",
        "ClearVision.Product/src/ClearVision.Product.Station",
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
        ".editorconfig",
        ".gitattributes",
        ".gitignore",
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

    [switch]$IncludeArchives,
    [string]$BaseRef,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$utf8Strict = [System.Text.UTF8Encoding]::new($false, $true)
try {
    [System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
} catch {
    # Windows PowerShell already has code pages; PowerShell 7 loads the provider when available.
}
$gbkStrict = [System.Text.Encoding]::GetEncoding(
    936,
    [System.Text.EncoderFallback]::ExceptionFallback,
    [System.Text.DecoderFallback]::ExceptionFallback)

function New-UnicodeString {
    param([int[]]$CodePoints)

    return -join ($CodePoints | ForEach-Object { [char]$_ })
}

$explicitMojibakeFragments = @(
    (New-UnicodeString @(0x59ab, 0x20ac)),
    (New-UnicodeString @(0x5bb8, 0x30e7)),
    (New-UnicodeString @(0x748b, 0x51ad, 0x762f)),
    (New-UnicodeString @(0x7ed4, 0xe21a)),
    (New-UnicodeString @(0x7f02, 0x4f7d)),
    (New-UnicodeString @(0x95c2, 0x581f)),
    (New-UnicodeString @(0x6fde, 0x64b3)),
    (New-UnicodeString @(0x5a62, 0x8dfa)),
    (New-UnicodeString @(0x5a34, 0xff48)),
    (New-UnicodeString @(0x93c9, 0x2542)),
    (New-UnicodeString @(0x95b8, 0x6393)),
    (New-UnicodeString @(0x940e, 0x6d99)),
    (New-UnicodeString @(0x9420, 0x5b2a)),
    (New-UnicodeString @(0x93b5, 0x0446)),
    (New-UnicodeString @(0x947e, 0x5cf0)),
    (New-UnicodeString @(0x7efe, 0x57ae, 0x7c2d)),
    (New-UnicodeString @(0x59ab, 0x20ac, 0x5a34)),
    (New-UnicodeString @(0x9352, 0x6d98, 0x7f13)),
    (New-UnicodeString @(0x5a34, 0x5b2d, 0x762f)),
    (New-UnicodeString @(0x9365, 0x60e7, 0x511a)),
    (New-UnicodeString @(0x59af, 0x2103, 0x6f98)),
    (New-UnicodeString @(0x74ba, 0xe21a, 0x7dde)),
    (New-UnicodeString @(0x93c8, 0x20ac)),
    (New-UnicodeString @(0x9417, 0x7470, 0x7ddb)),
    (New-UnicodeString @(0x9356, 0x5f52, 0x53a4)),
    (New-UnicodeString @(0x6434, 0x65c2, 0x6564, 0x59af, 0x2103, 0x6f98, 0x6fb6, 0x8fab, 0x89e6)),
    (New-UnicodeString @(0x7f01, 0x6a3a, 0x57d7, 0x6769, 0x70b4, 0x5e34, 0x7efe)),
    (New-UnicodeString @(0x7f02, 0x4f79, 0x00ea, 0x9369, 0x6941, 0x7a09, 0x701b, 0x6a3b, 0xe627)),
    (New-UnicodeString @(0x5a34, 0x5b2d, 0x762f)),
    (New-UnicodeString @(0x6d63, 0x6ec6, 0x20ac)),
    (New-UnicodeString @(0x94c7, 0x5470)),
    (New-UnicodeString @(0x7ee0, 0x6940, 0x74d9)),
    (New-UnicodeString @(0x93b5, 0x446, 0xe511)),
    (New-UnicodeString @(0x6fb6, 0x8fab, 0x89e6)),
    (New-UnicodeString @(0x95bf, 0x6b12, 0xe1e4)),
    (New-UnicodeString @(0x74d2, 0x546e, 0x6902)),
    (New-UnicodeString @(0x741a, 0xe0a2, 0x5f47, 0x5a11)),
    (New-UnicodeString @(0x748b, 0x51ad, 0x762f)),
    (New-UnicodeString @(0x68f0, 0x52ee, 0xe74d)),
    (New-UnicodeString @(0x9365, 0x60e7, 0x511a)),
    (New-UnicodeString @(0x93c3, 0x72b3, 0x7876)),
    (New-UnicodeString @(0x93c8, 0xe048, 0x20ac)),
    (New-UnicodeString @(0x7039, 0x5c7e, 0x579a)),
    (New-UnicodeString @(0x93c4, 0x5267, 0x305a)),
    (New-UnicodeString @(0x59af, 0x2033, 0x7037)),
    (New-UnicodeString @(0x6dc7, 0x6fc8, 0x6680)),
    (New-UnicodeString @(0x5a75, 0x20ac, 0x5a32)),
    (New-UnicodeString @(0x95c4, 0x5d84, 0x7d86)),
    (New-UnicodeString @(0x9351, 0x5fd3, 0x76af)),
    (New-UnicodeString @(0x6769, 0x70b4, 0x5e34)),
    (New-UnicodeString @(0x9359, 0x5d85, 0x7c2d)),
    (New-UnicodeString @(0x7efe, 0x3223, 0x58ca)),
    (New-UnicodeString @(0x6fe1, 0x509b, 0x7049)),
    (New-UnicodeString @(0x9359, 0x6828, 0x79f7)),
    (New-UnicodeString @(0x93cc, 0x30e6, 0x58d8)),
    (New-UnicodeString @(0x951b, 0x5746)),
    (New-UnicodeString @(0x951b, 0x5909)),
    (New-UnicodeString @(0x9286, 0x602d, 0x68, 0x61, 0x73, 0x65))
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
    "\test-results\",
    "\TestResults\",
    "\test_results\",
    "\build_err.txt",
    "\build_errors.txt",
    "\__pycache__\"
)

if (-not $IncludeArchives) {
    $excludedPathFragments += @(
        "\docs\archive\"
    )
}

function Resolve-CandidatePath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Get-RelativePath {
    param([string]$Path)

    $root = (Resolve-Path -LiteralPath $repoRoot).Path.TrimEnd("\", "/")
    $full = (Resolve-Path -LiteralPath $Path).Path
    if ($full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($root.Length).TrimStart("\", "/").Replace("\", "/")
    }

    return $full
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

function Test-TextCandidatePath {
    param([string]$Path)

    $name = [System.IO.Path]::GetFileName($Path)
    $extension = [System.IO.Path]::GetExtension($Path)
    if ($Extensions -contains $extension) {
        return $true
    }

    return $Extensions -contains $name
}

function Test-BinaryBytes {
    param([byte[]]$Bytes)

    $limit = [Math]::Min($Bytes.Length, 8192)
    for ($index = 0; $index -lt $limit; $index += 1) {
        if ($Bytes[$index] -eq 0) {
            return $true
        }
    }

    return $false
}

function Get-ChangedFiles {
    param([string]$Ref)

    $mergeBase = (& git -C $repoRoot -c core.quotepath=false merge-base $Ref HEAD).Trim()
    if ([string]::IsNullOrWhiteSpace($mergeBase)) {
        throw "Unable to resolve merge-base for '$Ref' and HEAD."
    }

    $paths = & git -C $repoRoot -c core.quotepath=false diff --name-only --diff-filter=ACMR "$mergeBase...HEAD"
    $files = New-Object System.Collections.Generic.List[string]
    foreach ($path in $paths) {
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        $resolved = Join-Path $repoRoot $path
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            continue
        }

        if (Test-ExcludedPath -Path $resolved) {
            continue
        }

        if (Test-TextCandidatePath -Path $resolved) {
            [void]$files.Add((Resolve-Path -LiteralPath $resolved).Path)
        }
    }

    return $files.ToArray()
}

function Get-RootFiles {
    $files = New-Object System.Collections.Generic.List[string]
    foreach ($root in $Roots) {
        $resolved = Resolve-CandidatePath -Path $root
        if (-not (Test-Path -LiteralPath $resolved)) {
            continue
        }

        $item = Get-Item -LiteralPath $resolved
        if (-not $item.PSIsContainer) {
            if ((Test-TextCandidatePath -Path $item.FullName) -and -not (Test-ExcludedPath -Path $item.FullName)) {
                [void]$files.Add($item.FullName)
            }
            continue
        }

        Get-ChildItem -LiteralPath $item.FullName -Recurse -File | ForEach-Object {
            if ((Test-TextCandidatePath -Path $_.FullName) -and -not (Test-ExcludedPath -Path $_.FullName)) {
                [void]$files.Add($_.FullName)
            }
        }
    }

    return $files.ToArray()
}

function Get-LineNumberAtOffset {
    param(
        [string]$Text,
        [int]$Offset
    )

    $line = 1
    for ($index = 0; $index -lt $Offset; $index += 1) {
        if ($Text[$index] -eq "`n") {
            $line += 1
        }
    }

    return $line
}

function ConvertFrom-Mojibake {
    param([string]$Text)

    if ($Text -notmatch '[^\x00-\x7F]') {
        return $null
    }

    try {
        $bytes = $gbkStrict.GetBytes($Text)
        return $utf8Strict.GetString($bytes)
    } catch {
        return $null
    }
}

function Get-MojibakeFindings {
    param(
        [string]$RelativePath,
        [string]$Text
    )

    $findings = New-Object System.Collections.Generic.List[object]
    $lines = $Text -split "`n", 0, "SimpleMatch"
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex += 1) {
        $line = $lines[$lineIndex].TrimEnd("`r")
        foreach ($fragment in $explicitMojibakeFragments) {
            $position = $line.IndexOf($fragment, [StringComparison]::Ordinal)
            while ($position -ge 0) {
                [void]$findings.Add([pscustomobject]@{
                    Path = $RelativePath
                    Line = $lineIndex + 1
                    Problem = "contains mojibake fragment '$fragment'"
                })

                $nextStart = $position + $fragment.Length
                if ($nextStart -ge $line.Length) {
                    break
                }

                $position = $line.IndexOf($fragment, $nextStart, [StringComparison]::Ordinal)
            }
        }

        $privateUseMatches = [regex]::Matches($line, '[\uE000-\uF8FF]')
        foreach ($match in $privateUseMatches) {
            [void]$findings.Add([pscustomobject]@{
                Path = $RelativePath
                Line = $lineIndex + 1
                Problem = ("contains private-use Unicode character U+{0:X4}, usually produced by mojibake" -f [int][char]$match.Value[0])
            })
        }

        $decoded = ConvertFrom-Mojibake -Text $line
        if ($null -ne $decoded -and
            $decoded -ne $line -and
            $decoded -match '[\u4e00-\u9fff]' -and
            $line -match '[\u4e00-\u9fff].*[\u4e00-\u9fff]') {
            [void]$findings.Add([pscustomobject]@{
                Path = $RelativePath
                Line = $lineIndex + 1
                Problem = "recoverable GBK/UTF-8 mojibake; decodes as '$decoded'"
            })
        }
    }

    return $findings.ToArray()
}

function Test-CandidateFile {
    param([string]$File)

    $relative = Get-RelativePath -Path $File
    $findings = New-Object System.Collections.Generic.List[object]
    $bytes = [System.IO.File]::ReadAllBytes($File)

    if (Test-BinaryBytes -Bytes $bytes) {
        return $findings.ToArray()
    }

    try {
        $text = $utf8Strict.GetString($bytes)
    } catch {
        [void]$findings.Add([pscustomobject]@{
            Path = $relative
            Line = 0
            Problem = "not valid UTF-8"
        })
        return $findings.ToArray()
    }

    $replacementMatches = [regex]::Matches($text, [string][char]0xfffd)
    foreach ($match in $replacementMatches) {
        [void]$findings.Add([pscustomobject]@{
            Path = $relative
            Line = Get-LineNumberAtOffset -Text $text -Offset $match.Index
            Problem = "contains U+FFFD replacement character"
        })
    }

    $mojibakeFindings = Get-MojibakeFindings -RelativePath $relative -Text $text
    foreach ($finding in $mojibakeFindings) {
        [void]$findings.Add($finding)
    }

    return $findings.ToArray()
}

function Invoke-TextEncodingSelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("cv-encoding-selftest-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    try {
        $validSingle = Join-Path $tempRoot "valid-single.md"
        $invalidUtf8 = Join-Path $tempRoot "invalid-utf8.md"
        $replacement = Join-Path $tempRoot "replacement.md"
        $multiMojibake = Join-Path $tempRoot "multi-mojibake.md"
        $validChinese = Join-Path $tempRoot "valid-chinese.md"

        $normalSingleChinese = New-UnicodeString @(0x6b63, 0x5e38, 0x4e2d, 0x6587, 0xff1a, 0x6765, 0x3001, 0x7ecf, 0x3002)
        $mojibakeText = (New-UnicodeString @(0x5bb8, 0x30e7, 0x25bc, 0x7ee0, 0xff04, 0x608a, 0x000a, 0x6dc7, 0x6fc6, 0x74e8, 0x5bb8, 0x30e7, 0x25bc, 0x000a, 0x7efe, 0x57ae, 0x7c2d, 0x59ab, 0x20ac, 0x5a34)) +
            "`n" + (New-UnicodeString @(0x7ee0, 0x6940, 0x74d9, 0x93b5, 0x0446, 0xe511, 0x6fb6, 0x8fab, 0x89e6)) +
            "`n" + (New-UnicodeString @(0x95c2, 0x56e3, 0x5053, 0xe627))
        $validChineseText = New-UnicodeString @(0x5de5, 0x7a0b, 0x4fdd, 0x5b58, 0x6210, 0x529f, 0xff0c, 0x6d41, 0x7a0b, 0x6570, 0x636e, 0x901a, 0x8fc7, 0x3002)

        [System.IO.File]::WriteAllText($validSingle, $normalSingleChinese, $utf8Strict)
        [System.IO.File]::WriteAllBytes($invalidUtf8, [byte[]](0x63, 0x76, 0x3a, 0xc3, 0x28))
        [System.IO.File]::WriteAllText($replacement, "bad $([char]0xfffd) char", $utf8Strict)
        [System.IO.File]::WriteAllText($multiMojibake, $mojibakeText, $utf8Strict)
        [System.IO.File]::WriteAllText($validChinese, $validChineseText, $utf8Strict)

        $validSingleFindings = @(Test-CandidateFile -File $validSingle)
        if ($validSingleFindings.Count -ne 0) {
            throw "SelfTest failed: normal single Chinese characters were reported."
        }

        $invalidFindings = @(Test-CandidateFile -File $invalidUtf8)
        if (-not ($invalidFindings | Where-Object { $_.Problem -eq "not valid UTF-8" })) {
            throw "SelfTest failed: invalid UTF-8 was not rejected."
        }

        $replacementFindings = @(Test-CandidateFile -File $replacement)
        if (-not ($replacementFindings | Where-Object { $_.Problem -like "contains U+FFFD*" })) {
            throw "SelfTest failed: U+FFFD was not rejected."
        }

        $mojibakeFindings = @(Test-CandidateFile -File $multiMojibake)
        if ($mojibakeFindings.Count -lt 2) {
            throw "SelfTest failed: multiple mojibake findings were not all reported."
        }

        $validChineseFindings = @(Test-CandidateFile -File $validChinese)
        if ($validChineseFindings.Count -ne 0) {
            throw "SelfTest failed: valid UTF-8 Chinese was reported."
        }
    } finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force
        }
    }

    Write-Host "Text encoding self-test passed."
}

if ($SelfTest) {
    Invoke-TextEncodingSelfTest
    return
}

if ([string]::IsNullOrWhiteSpace($BaseRef)) {
    $files = Get-RootFiles
} else {
    $files = Get-ChangedFiles -Ref $BaseRef
}

$failures = New-Object System.Collections.Generic.List[object]
foreach ($file in $files | Sort-Object -Unique) {
    $fileFindings = Test-CandidateFile -File $file
    foreach ($finding in $fileFindings) {
        [void]$failures.Add($finding)
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Text encoding scan failed:"
    foreach ($failure in $failures) {
        if ($failure.Line -gt 0) {
            Write-Host ("{0}:{1}: {2}" -f $failure.Path, $failure.Line, $failure.Problem)
        } else {
            Write-Host ("{0}: {1}" -f $failure.Path, $failure.Problem)
        }
    }
    throw "Text encoding scan failed with $($failures.Count) finding(s)."
}

Write-Host "Text encoding scan passed for $($files.Count) files."
