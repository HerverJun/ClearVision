# ClearVision Virtual MC/FINS PLC

This package provides local protocol-level virtual PLC servers for ClearVision's general PLC connection test.

It covers:

- Mitsubishi MC 3E TCP, default port `5002`
- Omron FINS/TCP, default port `9600`

S7 is intentionally not included here.

The implementation is pure Python standard library code and does not require `pip install`.

## Startup

From the repository root:

```powershell
& ".\scripts\start-virtual-mc-fins-plc.ps1"
```

Direct Python command:

```powershell
python tools/virtual-plc/mc-fins/virtual_plc_mc_fins.py --host 0.0.0.0 --mc-port 5002 --fins-port 9600
```

## Smoke Test

With the virtual PLC running:

```powershell
& ".\scripts\test-virtual-mc-fins-plc.ps1"
```

Success prints:

```text
Virtual MC/FINS PLC smoke test passed.
```

## ClearVision General PLC Connection Test

Use the software's general PLC connection test with these settings.

Mitsubishi MC:

```text
Protocol: MC
IpAddress: 127.0.0.1
Port: 5002
```

Omron FINS:

```text
Protocol: FINS
IpAddress: 127.0.0.1
Port: 9600
```

The endpoint path remains `/api/plc/test-connection`. The virtual servers respond to the current client ping reads:

```text
MC:   D0  -> 0x1234
FINS: DM0 -> 0x1234
```

## Optional .NET Verification

Start the virtual PLC first, then run:

```powershell
$env:CLEARVISION_RUN_VIRTUAL_MC_FINS_TESTS = "1"
$env:CLEARVISION_VIRTUAL_MC_HOST = "127.0.0.1"
$env:CLEARVISION_VIRTUAL_MC_PORT = "5002"
$env:CLEARVISION_VIRTUAL_FINS_HOST = "127.0.0.1"
$env:CLEARVISION_VIRTUAL_FINS_PORT = "9600"
& ".\scripts\run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName VirtualMcFinsPlcConnectionTests `
  -NoBuild `
  -NoRestore
```

When `CLEARVISION_RUN_VIRTUAL_MC_FINS_TESTS` is not `1`, these tests return immediately and do not connect to a PLC.

## Scope

This is a development test double, not a vendor PLC simulator. It implements the protocol surface currently needed by ClearVision's connection test and simple read/write smoke checks.
