[CmdletBinding()]
param(
    [string]$EventName = $env:GITHUB_EVENT_NAME,
    [string]$RefName = $env:GITHUB_REF_NAME,
    [string]$BeforeSha,
    [string]$PullRequestBaseSha,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$shaPattern = '^[0-9a-fA-F]{40}$'
$zeroShaPattern = '^0{40}$'

function Invoke-Git {
    param([string[]]$Arguments)

    $output = & git -c core.quotepath=false @Arguments 2>&1
    return @{
        ExitCode = $LASTEXITCODE
        Output = @($output)
    }
}

function Resolve-Commit {
    param([string]$Candidate)

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return $null
    }

    $trimmed = $Candidate.Trim()
    if ($trimmed -match $zeroShaPattern) {
        return $null
    }

    $result = Invoke-Git @("rev-parse", "--verify", "$trimmed^{commit}")
    if ($result.ExitCode -ne 0 -or $result.Output.Count -eq 0) {
        return $null
    }

    $sha = ([string]$result.Output[0]).Trim()
    if ($sha -notmatch $shaPattern -or $sha -match $zeroShaPattern) {
        return $null
    }

    return $sha.ToLowerInvariant()
}

function Resolve-CiDiffBase {
    param(
        [string]$ResolvedEventName,
        [string]$ResolvedRefName,
        [string]$ResolvedBeforeSha,
        [string]$ResolvedPullRequestBaseSha
    )

    $head = Resolve-Commit -Candidate "HEAD"
    if ([string]::IsNullOrWhiteSpace($head)) {
        throw "Unable to resolve HEAD for CI diff baseline."
    }

    $event = if ([string]::IsNullOrWhiteSpace($ResolvedEventName)) { "" } else { $ResolvedEventName.Trim() }
    $ref = if ([string]::IsNullOrWhiteSpace($ResolvedRefName)) { "" } else { $ResolvedRefName.Trim() }
    $base = $null

    if ($event -eq "pull_request") {
        $base = Resolve-Commit -Candidate $ResolvedPullRequestBaseSha
        if ([string]::IsNullOrWhiteSpace($base)) {
            throw "Unable to resolve pull request base SHA for CI diff baseline."
        }

        Write-Verbose "Resolved CI diff base from pull_request base SHA."
    } elseif ($event -eq "push") {
        $base = Resolve-Commit -Candidate $ResolvedBeforeSha
        if ([string]::IsNullOrWhiteSpace($base)) {
            if ($ref -eq "main") {
                $base = Resolve-Commit -Candidate "HEAD^"
                if ([string]::IsNullOrWhiteSpace($base)) {
                    throw "Unable to resolve HEAD^ fallback for main push CI diff baseline."
                }

                Write-Verbose "Resolved CI diff base from HEAD^ fallback for main push."
            } else {
                $base = Resolve-Commit -Candidate "origin/main"
                if ([string]::IsNullOrWhiteSpace($base)) {
                    throw "Unable to resolve origin/main fallback for push CI diff baseline."
                }

                Write-Verbose "Resolved CI diff base from origin/main fallback for non-main push."
            }
        } else {
            Write-Verbose "Resolved CI diff base from push before SHA."
        }
    } elseif ($event -eq "workflow_dispatch") {
        $base = Resolve-Commit -Candidate "origin/main"
        if ([string]::IsNullOrWhiteSpace($base)) {
            throw "Unable to resolve origin/main for workflow_dispatch CI diff baseline."
        }

        Write-Verbose "Resolved CI diff base from origin/main for workflow_dispatch."
    } else {
        $base = Resolve-Commit -Candidate "origin/main"
        if ([string]::IsNullOrWhiteSpace($base)) {
            throw "Unable to resolve origin/main for CI diff baseline."
        }

        Write-Verbose "Resolved CI diff base from origin/main for event '$event'."
    }

    if ($base -eq $head) {
        throw "Resolved CI diff baseline equals HEAD; refusing to run an empty committed-diff gate."
    }

    return $base
}

function Assert-Equal {
    param(
        [string]$Actual,
        [string]$Expected,
        [string]$Name
    )

    if ($Actual -ne $Expected) {
        throw "SelfTest failed: $Name returned '$Actual', expected '$Expected'."
    }
}

function Assert-Throws {
    param(
        [scriptblock]$Script,
        [string]$Name
    )

    try {
        & $Script
    } catch {
        return
    }

    throw "SelfTest failed: $Name did not fail closed."
}

function Invoke-CiDiffBaseSelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("clearvision-diff-base-" + [System.Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot | Out-Null

    try {
        Push-Location $tempRoot
        & git init -q | Out-Null
        & git config user.email "ci@example.invalid" | Out-Null
        & git config user.name "CI SelfTest" | Out-Null
        & git checkout -q -B main | Out-Null

        Set-Content -LiteralPath "file.txt" -Value "base" -Encoding utf8
        & git add file.txt | Out-Null
        & git commit -qm "base" | Out-Null
        $baseCommit = (& git rev-parse HEAD).Trim().ToLowerInvariant()

        Set-Content -LiteralPath "file.txt" -Value "main" -Encoding utf8
        & git add file.txt | Out-Null
        & git commit -qm "main" | Out-Null
        $mainCommit = (& git rev-parse HEAD).Trim().ToLowerInvariant()
        & git update-ref refs/remotes/origin/main $mainCommit | Out-Null

        & git checkout -q -B feature | Out-Null
        Set-Content -LiteralPath "feature.txt" -Value "feature" -Encoding utf8
        & git add feature.txt | Out-Null
        & git commit -qm "feature" | Out-Null
        $featureCommit = (& git rev-parse HEAD).Trim().ToLowerInvariant()

        $actual = Resolve-CiDiffBase -ResolvedEventName "pull_request" -ResolvedRefName "feature" -ResolvedBeforeSha "" -ResolvedPullRequestBaseSha $mainCommit
        Assert-Equal -Actual $actual -Expected $mainCommit -Name "pull_request base SHA"

        & git update-ref refs/remotes/origin/main $baseCommit | Out-Null
        $actual = Resolve-CiDiffBase -ResolvedEventName "push" -ResolvedRefName "feature" -ResolvedBeforeSha $mainCommit -ResolvedPullRequestBaseSha ""
        Assert-Equal -Actual $actual -Expected $mainCommit -Name "push before SHA"

        $actual = Resolve-CiDiffBase -ResolvedEventName "push" -ResolvedRefName "main" -ResolvedBeforeSha ("0" * 40) -ResolvedPullRequestBaseSha ""
        Assert-Equal -Actual $actual -Expected $mainCommit -Name "main push HEAD^ fallback"

        $actual = Resolve-CiDiffBase -ResolvedEventName "push" -ResolvedRefName "feature" -ResolvedBeforeSha ("0" * 40) -ResolvedPullRequestBaseSha ""
        Assert-Equal -Actual $actual -Expected $baseCommit -Name "branch push origin/main fallback"

        Assert-Throws -Name "missing pull request base" -Script {
            Resolve-CiDiffBase -ResolvedEventName "pull_request" -ResolvedRefName "feature" -ResolvedBeforeSha "" -ResolvedPullRequestBaseSha ""
        }

        & git update-ref refs/remotes/origin/main $featureCommit | Out-Null
        Assert-Throws -Name "empty diff baseline" -Script {
            Resolve-CiDiffBase -ResolvedEventName "workflow_dispatch" -ResolvedRefName "feature" -ResolvedBeforeSha "" -ResolvedPullRequestBaseSha ""
        }
    } finally {
        Pop-Location
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }

    Write-Host "CI diff base resolver self-test passed."
}

if ($SelfTest) {
    Invoke-CiDiffBaseSelfTest
    return
}

$resolvedBase = Resolve-CiDiffBase `
    -ResolvedEventName $EventName `
    -ResolvedRefName $RefName `
    -ResolvedBeforeSha $BeforeSha `
    -ResolvedPullRequestBaseSha $PullRequestBaseSha

Write-Output $resolvedBase
