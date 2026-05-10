# ClearVision Virtual Modbus TCP PLC

This package provides a local protocol-level Modbus TCP virtual PLC for ClearVision development. It is intended for testing `ModbusCommunicationOperator` when no physical PLC is available. It is not a full replacement for any vendor PLC simulator.

The server exposes:

- Read Coils
- Read Holding Registers
- Write Single Register
- Write Multiple Registers
- A small holding-register handshake flow

Docker Desktop / Docker for Windows is optional. Prefer the local Python virtual environment workflow unless Docker is already available in your environment.

## Local Python Startup

Windows PowerShell:

```powershell
cd tools/virtual-plc/modbus
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
python virtual_plc_modbus.py --host 0.0.0.0 --port 1502
```

Linux/macOS:

```bash
cd tools/virtual-plc/modbus
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
python virtual_plc_modbus.py --host 0.0.0.0 --port 1502
```

Convenience script from the repository root:

```powershell
& ".\scripts\start-virtual-modbus-plc.ps1"
```

## Smoke Test

With the server running:

```powershell
& ".\scripts\test-virtual-modbus-plc.ps1"
```

Direct Python command:

```bash
python tools/virtual-plc/modbus/test_client.py --host 127.0.0.1 --port 1502 --unit-id 1 --timeout 5
```

Success prints:

```text
Virtual Modbus PLC smoke test passed.
```

## Optional .NET Integration Tests

Start the virtual PLC first, then run the opt-in ClearVision tests:

```powershell
$env:CLEARVISION_RUN_VIRTUAL_PLC_TESTS = "1"
$env:CLEARVISION_VIRTUAL_MODBUS_HOST = "127.0.0.1"
$env:CLEARVISION_VIRTUAL_MODBUS_PORT = "1502"
$env:CLEARVISION_VIRTUAL_MODBUS_UNIT_ID = "1"
& ".\scripts\run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName ModbusCommunicationOperatorVirtualPlcTests `
  -NoBuild `
  -NoRestore
```

When `CLEARVISION_RUN_VIRTUAL_PLC_TESTS` is not `1`, these tests return immediately and do not connect to a PLC.

To run the combined PLC regression with the local virtual PLCs started and stopped by the script:

```powershell
& ".\scripts\run-tests-plc-regression.ps1" -Virtual -NoBuild -NoRestore -Verbosity minimal
```

## Docker Startup

Docker is only an optional path.

```bash
cd tools/virtual-plc/modbus
docker compose up --build
```

## Network And Proxy Notes

`pip install pymodbus` needs access to PyPI.

`docker compose build` may need access to Docker Hub and PyPI because the image uses `python:3.11-slim` and installs `pymodbus`.

If the network is restricted, configure `HTTP_PROXY` / `HTTPS_PROXY`, or use `PIP_INDEX_URL`. Example:

```bash
pip install -r requirements.txt -i https://pypi.tuna.tsinghua.edu.cn/simple
```

## Server Parameters

```bash
python virtual_plc_modbus.py \
  --host 0.0.0.0 \
  --port 1502 \
  --unit-id 1 \
  --cycle-ms 100 \
  --process-delay-ms 500 \
  --error-on-command 0
```

`--error-on-command 1` forces the next start command to return `HR1=500` and `HR4=1001`.

## Point Map

```text
Coil 0   APP_HEARTBEAT      reserved
Coil 1   PLC_HEARTBEAT      toggled by the virtual PLC
Coil 2   PLC_READY          always true
Coil 10  START_REQ_VIEW     HR0 == 1
Coil 11  START_ACK_VIEW     HR1 == 100
Coil 12  DONE_VIEW          HR1 == 200
Coil 13  ERROR_VIEW         HR1 == 500

HR 0     COMMAND            0=idle, 1=start, 9=reset
HR 1     STATUS             0=idle, 100=ack/running, 200=done, 500=error
HR 2     SEQUENCE           sequence written by ClearVision
HR 3     SEQUENCE_ECHO      sequence echoed by the virtual PLC
HR 4     ERROR_CODE         0=no error
HR 10    TEST_VALUE         default 1234
```

## ClearVision Operator Parameters

Base parameters for `ModbusCommunicationOperator`:

```text
Protocol: TCP
IpAddress: 127.0.0.1
Port: 1502
SlaveId: 1
TimeoutMs: 5000
```

Read Holding Register:

```text
FunctionCode: ReadHolding
RegisterAddress: 10
RegisterCount: 1
Expected Response: 1234
```

Write Single Register:

```text
FunctionCode: WriteSingle
RegisterAddress: 10
WriteValue: 5678
Expected Response: Write succeeded: 5678
```

Handshake:

```text
Step 1: WriteSingle HR2 = 123
Step 2: WriteSingle HR0 = 1
Step 3: ReadHolding HR1 until 200
Step 4: ReadHolding HR3, expect 123
Step 5: WriteSingle HR0 = 9
Step 6: ReadHolding HR1, expect 0
```

## Current Limits

ClearVision currently supports `ReadCoils`, `ReadHolding`, `WriteSingle Register`, and `WriteMultiple Registers` in `ModbusCommunicationOperator`.

The current operator does not support `WriteSingleCoil`, so the virtual PLC handshake uses holding registers and does not require writing coils.

`/api/plc/test-connection` currently supports `S7 / MC / FINS` only. It is not used for this Modbus virtual PLC test path.
