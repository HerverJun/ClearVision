[CmdletBinding()]
param(
    [string]$Path = ".",
    [string]$BaseRef,
    [switch]$IncludeUntracked,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$maxFileBytes = 5MB
$utf8Strict = [System.Text.UTF8Encoding]::new($false, $true)

$excludedDirectories = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    ".git",
    ".tmp",
    ".codex_tmp",
    ".vs",
    ".venv",
    "artifacts",
    "bin",
    "coverage",
    "node_modules",
    "nupkg",
    "obj",
    "playwright-report",
    "publish",
    "site-packages",
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
    ".onnx",
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

$combinedRulePattern = ($rules.Values -join "|")

$compiledRules = foreach ($rule in $rules.GetEnumerator()) {
    [pscustomobject]@{
        Name = $rule.Key
        Regex = [System.Text.RegularExpressions.Regex]::new(
            $rule.Value,
            [System.Text.RegularExpressions.RegexOptions]::Compiled)
    }
}

function Invoke-Git {
    param(
        [string]$RepoRoot,
        [string[]]$Arguments
    )

    $output = & git -C $RepoRoot -c core.quotepath=false @Arguments 2>&1
    return @{
        ExitCode = $LASTEXITCODE
        Output = @($output)
    }
}

function Resolve-RepositoryRoot {
    param([string]$CandidatePath)

    $resolved = (Resolve-Path -LiteralPath $CandidatePath).Path
    $result = Invoke-Git -RepoRoot $resolved -Arguments @("rev-parse", "--show-toplevel")
    if ($result.ExitCode -ne 0 -or $result.Output.Count -eq 0) {
        throw "Path is not inside a Git repository: $resolved"
    }

    return [string]$result.Output[0]
}

function Test-ExcludedPath {
    param([string]$RelativePath)

    foreach ($segment in ($RelativePath -split '[\\/]')) {
        if ($excludedDirectories.Contains($segment)) {
            return $true
        }
    }

    $extension = [System.IO.Path]::GetExtension($RelativePath)
    return $excludedExtensions.Contains($extension)
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

function Get-GitPathList {
    param(
        [string]$RepoRoot,
        [switch]$IncludeUntrackedFiles
    )

    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($mode in @("tracked", "untracked")) {
        if ($mode -eq "untracked" -and -not $IncludeUntrackedFiles) {
            continue
        }

        $arguments = if ($mode -eq "tracked") {
            @("ls-files", "--cached")
        } else {
            @("ls-files", "--others", "--exclude-standard")
        }

        $result = Invoke-Git -RepoRoot $RepoRoot -Arguments $arguments
        if ($result.ExitCode -ne 0) {
            throw "Unable to enumerate Git files: $($result.Output -join [Environment]::NewLine)"
        }

        foreach ($path in $result.Output) {
            if (-not [string]::IsNullOrWhiteSpace($path)) {
                [void]$paths.Add($path)
            }
        }
    }

    return $paths.ToArray() | Sort-Object -Unique
}

function Test-ShouldScanFile {
    param(
        [string]$RepoRoot,
        [string]$RelativePath
    )

    if (Test-ExcludedPath -RelativePath $RelativePath) {
        return $false
    }

    $fullPath = Join-Path $RepoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        return $false
    }

    $file = Get-Item -LiteralPath $fullPath
    if ($file.Length -gt $maxFileBytes) {
        return $false
    }

    $bytes = [System.IO.File]::ReadAllBytes($fullPath)
    return -not (Test-BinaryBytes -Bytes $bytes)
}

function Test-IgnoredLine {
    param([string]$Line)

    if ($null -eq $Line) {
        return $true
    }

    if ($Line.Length -gt 12000 -and $Line -match '^\s*"(image/[^"]+|application/octet-stream)"\s*:') {
        return $true
    }

    return $Line.Contains("<REDACTED>")
}

function Add-LineFindings {
    param(
        [System.Collections.Generic.List[object]]$Findings,
        [System.Collections.Generic.HashSet[string]]$FindingKeys,
        [string]$RelativePath,
        [int]$LineNumber,
        [string]$Line
    )

    if (Test-IgnoredLine -Line $Line) {
        return
    }

    foreach ($rule in $compiledRules) {
        if ($rule.Regex.IsMatch($Line)) {
            $key = "{0}`0{1}`0{2}" -f $RelativePath, $LineNumber, $rule.Name
            if ($FindingKeys.Add($key)) {
                $Findings.Add([pscustomobject]@{
                    Path = $RelativePath
                    Line = $LineNumber
                    Rule = $rule.Name
                })
            }
        }
    }
}

function Get-LineStartOffsets {
    param([string]$Text)

    $offsets = New-Object System.Collections.Generic.List[int]
    [void]$offsets.Add(0)
    for ($index = 0; $index -lt $Text.Length; $index += 1) {
        if ($Text[$index] -eq "`n") {
            [void]$offsets.Add($index + 1)
        }
    }

    return $offsets.ToArray()
}

function Get-LineNumberFromOffsets {
    param(
        [int[]]$Offsets,
        [int]$Offset
    )

    $index = [Array]::BinarySearch($Offsets, $Offset)
    if ($index -ge 0) {
        return $index + 1
    }

    return (-$index - 1)
}

function Get-LineAtNumber {
    param(
        [string]$Text,
        [int[]]$Offsets,
        [int]$LineNumber
    )

    $start = $Offsets[$LineNumber - 1]
    $end = if ($LineNumber -lt $Offsets.Length) { $Offsets[$LineNumber] } else { $Text.Length }
    return $Text.Substring($start, $end - $start).TrimEnd("`r", "`n")
}

function Scan-File {
    param(
        [string]$RepoRoot,
        [string]$RelativePath,
        [System.Collections.Generic.List[object]]$Findings,
        [System.Collections.Generic.HashSet[string]]$FindingKeys
    )

    if (-not (Test-ShouldScanFile -RepoRoot $RepoRoot -RelativePath $RelativePath)) {
        return
    }

    $fullPath = Join-Path $RepoRoot $RelativePath
    try {
        $text = [System.IO.File]::ReadAllText($fullPath, $utf8Strict)
        foreach ($rule in $compiledRules) {
            $matches = $rule.Regex.Matches($text)
            if ($matches.Count -eq 0) {
                continue
            }

            $lineOffsets = Get-LineStartOffsets -Text $text
            foreach ($match in $matches) {
                $lineNumber = Get-LineNumberFromOffsets -Offsets $lineOffsets -Offset $match.Index
                $line = Get-LineAtNumber -Text $text -Offsets $lineOffsets -LineNumber $lineNumber
                if (Test-IgnoredLine -Line $line) {
                    continue
                }

                $key = "{0}`0{1}`0{2}" -f $RelativePath, $lineNumber, $rule.Name
                if ($FindingKeys.Add($key)) {
                    $Findings.Add([pscustomobject]@{
                        Path = $RelativePath
                        Line = $lineNumber
                        Rule = $rule.Name
                    })
                }
            }
        }
    } catch {
        return
    }
}

function Scan-DiffAddedLines {
    param(
        [string]$RepoRoot,
        [string]$Ref,
        [System.Collections.Generic.List[object]]$Findings,
        [System.Collections.Generic.HashSet[string]]$FindingKeys
    )

    if ([string]::IsNullOrWhiteSpace($Ref)) {
        return
    }

    $mergeBaseResult = Invoke-Git -RepoRoot $RepoRoot -Arguments @("merge-base", $Ref, "HEAD")
    if ($mergeBaseResult.ExitCode -ne 0 -or $mergeBaseResult.Output.Count -eq 0) {
        throw "Unable to resolve merge-base for '$Ref' and HEAD."
    }

    $range = "$($mergeBaseResult.Output[0])...HEAD"
    $candidateResult = Invoke-Git -RepoRoot $RepoRoot -Arguments @("diff", "--name-only", "--diff-filter=ACMR", "-G$combinedRulePattern", $range)
    if ($candidateResult.ExitCode -ne 0) {
        throw "Unable to find candidate secret diff files for '$range'."
    }

    $candidatePaths = @($candidateResult.Output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($candidatePaths.Count -eq 0) {
        return
    }

    $diffArguments = @("diff", "--unified=0", "--diff-filter=ACMR", $range, "--") + $candidatePaths
    $diffResult = Invoke-Git -RepoRoot $RepoRoot -Arguments $diffArguments
    if ($diffResult.ExitCode -ne 0) {
        throw "Unable to read diff for '$range'."
    }

    $currentPath = $null
    $newLine = 0
    foreach ($rawLine in $diffResult.Output) {
        $line = [string]$rawLine
        if ($line.StartsWith("+++ b/", [StringComparison]::Ordinal)) {
            $currentPath = $line.Substring(6)
            continue
        }

        if ($line -match '^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@') {
            $newLine = [int]$Matches[1]
            continue
        }

        if ($null -eq $currentPath -or $line.Length -eq 0) {
            continue
        }

        $prefix = $line[0]
        if ($prefix -eq '+') {
            if (-not $line.StartsWith("+++", [StringComparison]::Ordinal) -and
                -not (Test-ExcludedPath -RelativePath $currentPath)) {
                Add-LineFindings -Findings $Findings -FindingKeys $FindingKeys -RelativePath $currentPath -LineNumber $newLine -Line $line.Substring(1)
            }
            $newLine += 1
        } elseif ($prefix -eq ' ') {
            $newLine += 1
        }
    }
}

function Invoke-SecretScanSelfTest {
    $findings = [System.Collections.Generic.List[object]]::new()
    $keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    Add-LineFindings -Findings $findings -FindingKeys $keys -RelativePath "fixture.txt" -LineNumber 1 -Line ("token=" + "sk-" + ("A" * 24))
    if ($findings.Count -ne 1 -or $findings[0].Rule -ne "OpenAI/compatible sk token") {
        throw "Secret scan self-test failed: fixture token was not detected."
    }

    $cleanFindings = [System.Collections.Generic.List[object]]::new()
    $cleanKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    Add-LineFindings -Findings $cleanFindings -FindingKeys $cleanKeys -RelativePath "fixture.txt" -LineNumber 1 -Line "token=<REDACTED>"
    if ($cleanFindings.Count -ne 0) {
        throw "Secret scan self-test failed: redacted placeholder was reported."
    }

    Write-Host "Secret scan self-test passed."
}

if ($SelfTest) {
    Invoke-SecretScanSelfTest
    return
}

$root = Resolve-RepositoryRoot -CandidatePath $Path
$findings = [System.Collections.Generic.List[object]]::new()
$findingKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

$files = Get-GitPathList -RepoRoot $root -IncludeUntrackedFiles:$IncludeUntracked
foreach ($file in $files) {
    Scan-File -RepoRoot $root -RelativePath $file -Findings $findings -FindingKeys $findingKeys
}

Scan-DiffAddedLines -RepoRoot $root -Ref $BaseRef -Findings $findings -FindingKeys $findingKeys

if ($findings.Count -gt 0) {
    Write-Host "Secret scan failed. Potential secret locations:"
    foreach ($finding in $findings | Sort-Object Path, Line, Rule) {
        Write-Host ("{0}:{1}: {2}" -f $finding.Path, $finding.Line, $finding.Rule)
    }
    Write-Host "Full secret values are intentionally not printed."
    throw "Secret scan failed with $($findings.Count) potential secret(s)."
}

$mode = if ($IncludeUntracked) { "tracked and unignored untracked" } else { "tracked" }
Write-Host "Secret scan passed: no high-confidence secret patterns found in $($files.Count) $mode file(s)."
