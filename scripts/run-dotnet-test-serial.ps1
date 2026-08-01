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

    [string[]]$Collect,

    [int]$MinimumTotalTests = 0,

    [int]$MinimumPassedTests = 0,

    [int]$LockWaitSeconds = 30,

    [switch]$ReturnExitCode,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$DotNetTestArguments
)

$ErrorActionPreference = "Stop"

$dotnetShimPath = Join-Path $PSScriptRoot "dotnet.ps1"
. (Join-Path $PSScriptRoot "trx-validation.ps1")
$dotnetPathOutput = & $dotnetShimPath -InstallIfMissing -PrintPath -ReturnExitCode
if ($LASTEXITCODE -ne 0) {
    throw "Unable to resolve repository .NET SDK with $dotnetShimPath."
}

$dotnetPath = ($dotnetPathOutput | Select-Object -Last 1).Trim()
if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
    throw "Resolved dotnet path is empty."
}

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

function Test-TrxCounters {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Counters,

        [int]$RequiredTotal,

        [int]$RequiredPassed
    )

    $validation = Test-TrxGreen -Counters $Counters -RequiredTotal $RequiredTotal
    if ($RequiredPassed -gt 0 -and $Counters.Passed -lt $RequiredPassed) {
        $validation.completeGreen = $false
        $validation.issues += "passed=$($Counters.Passed) is below minimum=$RequiredPassed"
    }

    if (-not $validation.completeGreen) {
        foreach ($issue in $validation.issues) {
            Write-Host "[dotnet-test] TRX validation failed: $issue"
        }
    }

    return [bool]$validation.completeGreen
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

    foreach ($collector in $Collect) {
        if (-not [string]::IsNullOrWhiteSpace($collector)) {
            $arguments += @("--collect", $collector)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($effectiveFilter)) {
        $arguments += @("--filter", $effectiveFilter)
    }

    foreach ($extraArgument in $DotNetTestArguments) {
        if (-not [string]::IsNullOrWhiteSpace($extraArgument)) {
            $arguments += $extraArgument
        }
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
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            & $dotnetPath @arguments 2>&1 | ForEach-Object { Write-Host $_ }
            $processExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
    }
    else {
        & $dotnetPath @arguments
        $processExitCode = $LASTEXITCODE
    }

    $exitCode = $processExitCode

    if ($exitCode -eq 0 -and ($MinimumTotalTests -gt 0 -or $MinimumPassedTests -gt 0)) {
        $requiredPassed = $MinimumPassedTests
        $trxPath = Join-Path $resolvedResultsDirectory $LogFileName

        try {
            $counters = Get-TrxCounters -Path $trxPath
            Write-Host "[dotnet-test] TRX counters: total=$($counters.Total), executed=$($counters.Executed), passed=$($counters.Passed), notExecuted=$($counters.NotExecuted), failed=$($counters.Failed), error=$($counters.Error), timeout=$($counters.Timeout), aborted=$($counters.Aborted)."

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
    return
}

exit $exitCode
