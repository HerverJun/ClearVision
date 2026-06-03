param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [string]$Configuration,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [string]$ResultsDirectory,

    [string]$LogFileName,

    [int]$MinimumTotalTests = 0,

    [switch]$Virtual,

    [int]$ModbusPort = 1502,

    [int]$McPort = 5002,

    [int]$FinsPort = 9600,

    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$runner = Join-Path $scriptRoot "run-dotnet-test-serial.ps1"
$project = Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.Tests\ClearVision.Product.Tests.csproj"

$startedProcesses = New-Object System.Collections.Generic.List[System.Diagnostics.Process]

function Wait-UntilVirtualPlcReady {
    param(
        [scriptblock]$Probe,
        [string]$Name,
        [int]$TimeoutSeconds = 20
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            & $Probe
            if ($LASTEXITCODE -eq 0) {
                return
            }
        } catch {
            # Keep probing until the startup deadline.
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw "$Name virtual PLC did not become ready within $TimeoutSeconds seconds."
}

function Ensure-ModbusPython {
    $modbusWorkdir = Join-Path $repoRoot "tools\virtual-plc\modbus"
    $python = Join-Path $modbusWorkdir ".venv\Scripts\python.exe"

    Push-Location $modbusWorkdir
    try {
        if (-not (Test-Path $python)) {
            python -m venv .venv *> $null
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to create Modbus virtual PLC Python environment."
            }
        }

        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            & $python -c "import pymodbus" *> $null
            $hasPymodbus = $LASTEXITCODE -eq 0
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }

        if (-not $hasPymodbus) {
            & $python -m ensurepip --upgrade --default-pip *> $null
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to bootstrap pip for Modbus virtual PLC."
            }

            & $python -m pip install -r requirements.txt *> $null
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to install Modbus virtual PLC Python dependencies."
            }
        }
    }
    finally {
        Pop-Location
    }

    return $python
}

function Start-VirtualPlcProcesses {
    $modbusWorkdir = Join-Path $repoRoot "tools\virtual-plc\modbus"
    $mcFinsWorkdir = Join-Path $repoRoot "tools\virtual-plc\mc-fins"
    $modbusPython = Ensure-ModbusPython

    $modbusProcess = Start-Process `
        -FilePath $modbusPython `
        -ArgumentList @(
            "virtual_plc_modbus.py",
            "--host", "127.0.0.1",
            "--port", "$ModbusPort",
            "--unit-id", "1"
        ) `
        -WorkingDirectory $modbusWorkdir `
        -WindowStyle Hidden `
        -PassThru
    $startedProcesses.Add($modbusProcess)

    $mcFinsProcess = Start-Process `
        -FilePath "python" `
        -ArgumentList @(
            "virtual_plc_mc_fins.py",
            "--host", "127.0.0.1",
            "--mc-port", "$McPort",
            "--fins-port", "$FinsPort"
        ) `
        -WorkingDirectory $mcFinsWorkdir `
        -WindowStyle Hidden `
        -PassThru
    $startedProcesses.Add($mcFinsProcess)

    Wait-UntilVirtualPlcReady -Name "Modbus" -Probe {
        & (Join-Path $scriptRoot "test-virtual-modbus-plc.ps1") -HostAddress "127.0.0.1" -Port $ModbusPort -UnitId 1 -TimeoutSeconds 2
    }

    Wait-UntilVirtualPlcReady -Name "MC/FINS" -Probe {
        & (Join-Path $scriptRoot "test-virtual-mc-fins-plc.ps1") -HostAddress "127.0.0.1" -McPort $McPort -FinsPort $FinsPort -TimeoutSeconds 2
    }

    $env:CLEARVISION_RUN_VIRTUAL_PLC_TESTS = "1"
    $env:CLEARVISION_VIRTUAL_MODBUS_HOST = "127.0.0.1"
    $env:CLEARVISION_VIRTUAL_MODBUS_PORT = "$ModbusPort"
    $env:CLEARVISION_VIRTUAL_MODBUS_UNIT_ID = "1"
    $env:CLEARVISION_RUN_VIRTUAL_MC_FINS_TESTS = "1"
    $env:CLEARVISION_VIRTUAL_MC_HOST = "127.0.0.1"
    $env:CLEARVISION_VIRTUAL_MC_PORT = "$McPort"
    $env:CLEARVISION_VIRTUAL_FINS_HOST = "127.0.0.1"
    $env:CLEARVISION_VIRTUAL_FINS_PORT = "$FinsPort"
}

$parameters = @{
    Project = $project
    Filter = "FullyQualifiedName~PlcComm"
    Verbosity = $Verbosity
}

if ($Virtual) {
    Start-VirtualPlcProcesses
    $parameters.Filter = "FullyQualifiedName~PlcComm|FullyQualifiedName~ModbusCommunicationOperatorVirtualPlcTests"

    if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
        $ResultsDirectory = Join-Path $repoRoot (".tmp\plc-regression\{0:yyyyMMdd-HHmmss}" -f (Get-Date))
    }
}

if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $parameters.ResultsDirectory = $ResultsDirectory
}

if (-not [string]::IsNullOrWhiteSpace($LogFileName)) {
    $parameters.LogFileName = $LogFileName
}

if ($MinimumTotalTests -gt 0) {
    $parameters.MinimumTotalTests = $MinimumTotalTests
}

if (-not [string]::IsNullOrWhiteSpace($Configuration)) {
    $parameters.Configuration = $Configuration
}

if ($NoBuild) {
    $parameters.NoBuild = $true
}

if ($NoRestore) {
    $parameters.NoRestore = $true
}

$parameters.ReturnExitCode = $true

$testExitCode = 0
try {
    & $runner @parameters
    $testExitCode = $LASTEXITCODE
}
finally {
    foreach ($process in $startedProcesses) {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

$global:LASTEXITCODE = $testExitCode

if ($ReturnExitCode) {
    return
}

exit $testExitCode
