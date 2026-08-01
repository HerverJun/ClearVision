function Get-TrxCounterAttribute {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlElement]$Node,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $raw = $Node.GetAttribute($Name)
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return 0
    }

    $value = 0
    $parsed = [int]::TryParse(
        $raw,
        [Globalization.NumberStyles]::Integer,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref]$value)
    if (-not $parsed -or $value -lt 0) {
        throw "TRX Counters.$Name is not a non-negative integer: '$raw'."
    }

    return $value
}

function Test-TrxCounterAttributePresent {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlElement]$Node,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    return $Node.HasAttribute($Name)
}

function Get-TrxObservedOutcomeCounts {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlDocument]$Document
    )

    $counts = [ordered]@{
        total = 0
        executed = 0
        passed = 0
        failed = 0
        error = 0
        timeout = 0
        aborted = 0
        inconclusive = 0
        passedButRunAborted = 0
        notRunnable = 0
        notExecuted = 0
        disconnected = 0
        warning = 0
        completed = 0
        inProgress = 0
        pending = 0
    }

    $resultNodes = @($Document.SelectNodes("//*[local-name()='UnitTestResult']"))
    $counts.total = $resultNodes.Count
    foreach ($resultNode in $resultNodes) {
        $outcome = ([string]$resultNode.GetAttribute("outcome")).Trim()
        switch -Regex ($outcome) {
            '^Passed$' { $counts.passed++; continue }
            '^Failed$' { $counts.failed++; continue }
            '^Error$' { $counts.error++; continue }
            '^Timeout$' { $counts.timeout++; continue }
            '^Aborted$' { $counts.aborted++; continue }
            '^PassedButRunAborted$' { $counts.passedButRunAborted++; continue }
            '^Inconclusive$' { $counts.inconclusive++; continue }
            '^NotRunnable$' { $counts.notRunnable++; continue }
            '^(NotExecuted|Skipped)$' { $counts.notExecuted++; continue }
            '^Disconnected$' { $counts.disconnected++; continue }
            '^Warning$' { $counts.warning++; continue }
            '^Completed$' { $counts.completed++; continue }
            '^InProgress$' { $counts.inProgress++; continue }
            '^Pending$' { $counts.pending++; continue }
        }
    }

    $counts.executed = $counts.total - $counts.notExecuted
    return $counts
}

function Get-TrxSkipReason {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlElement]$ResultNode
    )

    $reasonParts = @(
        $ResultNode.SelectNodes(".//*[local-name()='StdOut' or local-name()='Message']") |
            ForEach-Object { ([string]$_.InnerText).Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($reasonParts.Count -gt 0) {
        return ($reasonParts -join " | ")
    }

    return "No skip reason was emitted in TRX."
}

function Get-TrxSkipTests {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlDocument]$Document
    )

    $skipped = New-Object System.Collections.Generic.List[object]
    foreach ($resultNode in @($Document.SelectNodes("//*[local-name()='UnitTestResult']"))) {
        $outcome = ([string]$resultNode.GetAttribute("outcome")).Trim()
        if ($outcome -notin @("NotExecuted", "Skipped")) {
            continue
        }

        $testName = ([string]$resultNode.GetAttribute("testName")).Trim()
        if ([string]::IsNullOrWhiteSpace($testName)) {
            $testName = ([string]$resultNode.GetAttribute("testId")).Trim()
        }

        $skipped.Add([ordered]@{
            testName = $testName
            testId = ([string]$resultNode.GetAttribute("testId")).Trim()
            outcome = $outcome
            reason = Get-TrxSkipReason -ResultNode $resultNode
        })
    }

    return ,$skipped.ToArray()
}

function Get-TrxCounters {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Expected TRX file was not produced: $Path"
    }

    [xml]$trx = Get-Content -LiteralPath $Path -Raw
    $counterNode = $trx.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -eq $counterNode) {
        throw "TRX file does not contain a Counters node: $Path"
    }

    $counterNames = @(
        "total", "executed", "passed", "failed", "error", "timeout", "aborted",
        "inconclusive", "passedButRunAborted", "notRunnable", "notExecuted",
        "disconnected", "warning", "completed", "inProgress", "pending"
    )
    $reported = [ordered]@{}
    $present = [ordered]@{}
    foreach ($name in $counterNames) {
        $reported[$name] = Get-TrxCounterAttribute -Node $counterNode -Name $name
        $present[$name] = Test-TrxCounterAttributePresent -Node $counterNode -Name $name
    }

    $observed = Get-TrxObservedOutcomeCounts -Document $trx
    $skippedTests = Get-TrxSkipTests -Document $trx
    $effective = [ordered]@{}
    foreach ($name in $counterNames) {
        $effective[$name] = [Math]::Max([int]$reported[$name], [int]$observed[$name])
    }

    $nonSuccessCounters = [ordered]@{
        failed = $effective.failed
        error = $effective.error
        timeout = $effective.timeout
        aborted = $effective.aborted
        inconclusive = $effective.inconclusive
        notRunnable = $effective.notRunnable
        disconnected = $effective.disconnected
        warning = $effective.warning
        inProgress = $effective.inProgress
        pending = $effective.pending
        passedButRunAborted = $effective.passedButRunAborted
    }

    return [ordered]@{
        total = $effective.total
        executed = $effective.executed
        passed = $effective.passed
        notExecuted = $effective.notExecuted
        failed = $effective.failed
        error = $effective.error
        timeout = $effective.timeout
        aborted = $effective.aborted
        inconclusive = $effective.inconclusive
        passedButRunAborted = $effective.passedButRunAborted
        notRunnable = $effective.notRunnable
        disconnected = $effective.disconnected
        warning = $effective.warning
        completed = $effective.completed
        inProgress = $effective.inProgress
        pending = $effective.pending
        skippedTests = $skippedTests
        nonSuccessCounters = $nonSuccessCounters
        reported = $reported
        present = $present
        observed = $observed
        effective = $effective
        resultCount = $observed.total
    }
}

function Test-TrxGreen {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Counters,

        [int]$RequiredTotal = 0
    )

    $issues = New-Object System.Collections.Generic.List[string]
    if ($RequiredTotal -gt 0 -and [int]$Counters.total -lt $RequiredTotal) {
        $issues.Add("total=$($Counters.total) is below minimum=$RequiredTotal")
    }

    if ([int]$Counters.total -ne ([int]$Counters.executed + [int]$Counters.notExecuted)) {
        $issues.Add("total=$($Counters.total) does not equal executed=$($Counters.executed)+notExecuted=$($Counters.notExecuted)")
    }

    if ([int]$Counters.executed -ne [int]$Counters.passed) {
        $issues.Add("executed=$($Counters.executed) does not equal passed=$($Counters.passed)")
    }

    foreach ($property in @(
        "failed", "error", "timeout", "aborted", "inconclusive", "notRunnable",
        "disconnected", "warning", "inProgress", "pending", "passedButRunAborted"
    )) {
        if ([int]$Counters.nonSuccessCounters.$property -ne 0) {
            $issues.Add("$property=$($Counters.nonSuccessCounters.$property)")
        }
    }

    if ([int]$Counters.resultCount -ne [int]$Counters.total) {
        $issues.Add("TRX UnitTestResult count=$($Counters.resultCount) does not equal total=$($Counters.total)")
    }

    foreach ($property in @(
        "total", "executed", "passed", "failed", "error", "timeout", "aborted",
        "inconclusive", "passedButRunAborted", "notRunnable", "disconnected", "warning", "completed",
        "inProgress", "pending"
    )) {
        if (-not [bool]$Counters.present.$property) {
            continue
        }

        if ([int]$Counters.reported.$property -ne [int]$Counters.observed.$property) {
            $issues.Add("Counters.$property=$($Counters.reported.$property) disagrees with observed=$($Counters.observed.$property)")
        }
    }

    return [ordered]@{
        completeGreen = ($issues.Count -eq 0)
        issues = @($issues)
        nonSuccessCounters = $Counters.nonSuccessCounters
    }
}
