param(
    [Parameter(Mandatory = $true)]
    [string]$Project,

    [string[]]$FullyQualifiedName,

    [string]$Filter,

    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [string]$Configuration,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [string]$ResultsDirectory,

    [string]$LogFileName,

    [int]$MinimumTotalTests = 0,

    [int]$MinimumPassedTests = 0,

    [int]$LockWaitSeconds = 30,

    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"

function Quote-Argument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ($Value -match '[\s"`|]') {
        return '"' + ($Value -replace '"', '\"') + '"'
    }

    return $Value
}

function Get-TrxCounters {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Expected TRX file was not produced: $Path"
    }

    [xml]$trx = Get-Content -LiteralPath $Path -Raw
    $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -eq $counters) {
        throw "TRX file does not contain a Counters node: $Path"
    }

    return [PSCustomObject]@{
        Total = [int]$counters.total
        Executed = [int]$counters.executed
        Passed = [int]$counters.passed
        Failed = [int]$counters.failed
        Error = [int]$counters.error
        Timeout = [int]$counters.timeout
        Aborted = [int]$counters.aborted
    }
}

function Test-TrxCounters {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Counters,

        [int]$RequiredTotal,

        [int]$RequiredPassed
    )

    $passed = $true
    if ($RequiredTotal -gt 0 -and $Counters.Total -lt $RequiredTotal) {
        Write-Host "[dotnet-test] TRX validation failed: total tests $($Counters.Total) is below required minimum $RequiredTotal."
        $passed = $false
    }

    if ($RequiredTotal -gt 0 -and $Counters.Executed -lt $RequiredTotal) {
        Write-Host "[dotnet-test] TRX validation failed: executed tests $($Counters.Executed) is below required minimum $RequiredTotal."
        $passed = $false
    }

    if ($RequiredPassed -gt 0 -and $Counters.Passed -lt $RequiredPassed) {
        Write-Host "[dotnet-test] TRX validation failed: passed tests $($Counters.Passed) is below required minimum $RequiredPassed."
        $passed = $false
    }

    if (($Counters.Failed + $Counters.Error + $Counters.Timeout + $Counters.Aborted) -gt 0) {
        Write-Host "[dotnet-test] TRX validation failed: failed=$($Counters.Failed), error=$($Counters.Error), timeout=$($Counters.Timeout), aborted=$($Counters.Aborted)."
        $passed = $false
    }

    return $passed
}

if ($FullyQualifiedName.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($Filter)) {
    throw "Specify either -FullyQualifiedName or -Filter, not both."
}

if (-not [string]::IsNullOrWhiteSpace($LogFileName) -and [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    throw "Specify -ResultsDirectory when using -LogFileName."
}

if (($MinimumTotalTests -gt 0 -or $MinimumPassedTests -gt 0) -and ([string]::IsNullOrWhiteSpace($ResultsDirectory) -or [string]::IsNullOrWhiteSpace($LogFileName))) {
    throw "Specify -ResultsDirectory and -LogFileName when using minimum test count validation."
}

$currentProcess = $null
try {
    $currentProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $PID" -ErrorAction Stop
}
catch {
    Write-Host "[dotnet-test] Unable to inspect process command line; skipping -File invocation check."
}

if ($null -ne $currentProcess -and $currentProcess.CommandLine -like '*-File*run-dotnet-test-serial.ps1*') {
    throw "Invoke this script from the current PowerShell shell with: & './scripts/run-dotnet-test-serial.ps1' ... . Do not wrap it with 'powershell.exe -File', because Codex can hang on leaked child processes in that mode."
}

$normalizedFullyQualifiedName = @()
foreach ($value in $FullyQualifiedName) {
    if ([string]::IsNullOrWhiteSpace($value)) {
        continue
    }

    foreach ($part in ($value -split ',')) {
        $trimmedPart = $part.Trim()
        if (-not [string]::IsNullOrWhiteSpace($trimmedPart)) {
            $normalizedFullyQualifiedName += $trimmedPart
        }
    }
}

$resolvedProject = Resolve-Path -LiteralPath $Project
$projectPath = $resolvedProject.Path
$projectKey = $projectPath.ToLowerInvariant()
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($projectKey))
$hashText = [System.BitConverter]::ToString($hashBytes).Replace("-", "")
$mutexName = "Global\ClearVision.DotNetTest." + $hashText.Substring(0, 24)
$mutex = [System.Threading.Mutex]::new($false, $mutexName)
$lockAcquired = $false
$exitCode = 0

try {
    Write-Host "[dotnet-test] Waiting for project lock: $projectPath"

    try {
        $lockAcquired = $mutex.WaitOne([TimeSpan]::FromSeconds([Math]::Max($LockWaitSeconds, 0)))
    }
    catch [System.Threading.AbandonedMutexException] {
        $lockAcquired = $true
    }

    if (-not $lockAcquired) {
        throw "Timed out after $LockWaitSeconds seconds waiting to run dotnet test for $projectPath. Another run for the same project is still active."
    }

    $effectiveFilter = $Filter
    if ($normalizedFullyQualifiedName.Count -gt 0) {
        $filterParts = $normalizedFullyQualifiedName |
            ForEach-Object { "FullyQualifiedName~$_" }

        $effectiveFilter = $filterParts -join "|"
    }

    $arguments = @(
        "test"
        $projectPath
        "--nologo"
        "--verbosity"
        $Verbosity
    )

    if ($NoBuild) {
        $arguments += "--no-build"
    }

    if ($NoRestore) {
        $arguments += "--no-restore"
    }

    if (-not [string]::IsNullOrWhiteSpace($Configuration)) {
        $arguments += @("--configuration", $Configuration)
    }

    $resolvedResultsDirectory = $null
    if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
        $resultsDirectoryPath = if ([System.IO.Path]::IsPathRooted($ResultsDirectory)) {
            $ResultsDirectory
        }
        else {
            Join-Path (Get-Location).Path $ResultsDirectory
        }

        $resolvedResultsDirectory = [System.IO.Path]::GetFullPath($resultsDirectoryPath)
        [System.IO.Directory]::CreateDirectory($resolvedResultsDirectory) | Out-Null
        $arguments += @("--results-directory", $resolvedResultsDirectory)
    }

    if (-not [string]::IsNullOrWhiteSpace($LogFileName)) {
        $arguments += @("--logger", "trx;LogFileName=$LogFileName")
    }

    if (-not [string]::IsNullOrWhiteSpace($effectiveFilter)) {
        $arguments += @("--filter", $effectiveFilter)
    }

    $preview = "dotnet " + (($arguments | ForEach-Object { Quote-Argument $_ }) -join " ")
    Write-Host "[dotnet-test] Acquired project lock."

    if ($normalizedFullyQualifiedName.Count -gt 0) {
        Write-Host "[dotnet-test] Combined $($normalizedFullyQualifiedName.Count) FullyQualifiedName filters into one invocation."
    }

    if (-not [string]::IsNullOrWhiteSpace($resolvedResultsDirectory)) {
        Write-Host "[dotnet-test] Results directory: $resolvedResultsDirectory"
    }

    if (-not [string]::IsNullOrWhiteSpace($LogFileName)) {
        Write-Host "[dotnet-test] TRX log: $LogFileName"
    }

    Write-Host "[dotnet-test] $preview"

    if ($ReturnExitCode) {
        & dotnet @arguments 2>&1 | ForEach-Object { Write-Host $_ }
    }
    else {
        & dotnet @arguments
    }

    $exitCode = $LASTEXITCODE

    if ($exitCode -eq 0 -and ($MinimumTotalTests -gt 0 -or $MinimumPassedTests -gt 0)) {
        $requiredPassed = if ($MinimumPassedTests -gt 0) { $MinimumPassedTests } else { $MinimumTotalTests }
        $trxPath = Join-Path $resolvedResultsDirectory $LogFileName

        try {
            $counters = Get-TrxCounters -Path $trxPath
            Write-Host "[dotnet-test] TRX counters: total=$($counters.Total), executed=$($counters.Executed), passed=$($counters.Passed), failed=$($counters.Failed), error=$($counters.Error), timeout=$($counters.Timeout), aborted=$($counters.Aborted)."

            if (-not (Test-TrxCounters -Counters $counters -RequiredTotal $MinimumTotalTests -RequiredPassed $requiredPassed)) {
                $exitCode = 1
            }
            else {
                Write-Host "[dotnet-test] TRX validation passed (minimum total=$MinimumTotalTests, minimum passed=$requiredPassed)."
            }
        }
        catch {
            Write-Host "[dotnet-test] TRX validation failed: $($_.Exception.Message)"
            $exitCode = 1
        }
    }
}
finally {
    $sha256.Dispose()

    if ($lockAcquired) {
        [void]$mutex.ReleaseMutex()
    }

    $mutex.Dispose()
}

$global:LASTEXITCODE = $exitCode

if ($ReturnExitCode) {
    return $exitCode
}

exit $exitCode
