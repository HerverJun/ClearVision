[CmdletBinding()]
param(
    [string]$BaseRef = "origin/main"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Git {
    param([string[]]$Arguments)

    $output = & git -C $repoRoot -c core.quotepath=false @Arguments 2>&1
    return @{
        ExitCode = $LASTEXITCODE
        Output = @($output)
    }
}

$mergeBaseResult = Invoke-Git @("merge-base", $BaseRef, "HEAD")
if ($mergeBaseResult.ExitCode -ne 0 -or $mergeBaseResult.Output.Count -eq 0) {
    $details = ($mergeBaseResult.Output -join [Environment]::NewLine)
    throw "Unable to resolve merge-base for '$BaseRef' and HEAD. $details"
}

$mergeBase = [string]$mergeBaseResult.Output[0]
$range = "$mergeBase...HEAD"
$failures = New-Object System.Collections.Generic.List[string]

$whitespaceResult = Invoke-Git @("diff", "--check", $range)
foreach ($line in $whitespaceResult.Output) {
    if ($line -match '^.+:\d+:') {
        [void]$failures.Add($line)
    }
}

if ($whitespaceResult.ExitCode -ne 0 -and $failures.Count -eq 0) {
    $details = ($whitespaceResult.Output -join [Environment]::NewLine)
    throw "Unable to run git diff --check for '$range'. $details"
}

$diffResult = Invoke-Git @("diff", "--unified=0", "--no-ext-diff", $range)
if ($diffResult.ExitCode -ne 0) {
    $details = ($diffResult.Output -join [Environment]::NewLine)
    throw "Unable to read diff for '$range'. $details"
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
        if (-not $line.StartsWith("+++", [StringComparison]::Ordinal)) {
            $content = $line.Substring(1)
            if ($content -match '^(<<<<<<<|=======|>>>>>>>)(\s|$)') {
                [void]$failures.Add(("{0}:{1}: merge conflict marker" -f $currentPath, $newLine))
            }
            $newLine += 1
        }
    } elseif ($prefix -eq ' ') {
        $newLine += 1
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Diff hygiene check failed:"
    foreach ($failure in $failures) {
        Write-Host $failure
    }

    throw "Diff hygiene check failed with $($failures.Count) finding(s)."
}

Write-Host "Diff hygiene check passed for $range (BaseRef=$BaseRef)."
