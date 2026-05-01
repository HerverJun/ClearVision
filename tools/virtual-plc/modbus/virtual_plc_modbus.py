#!/usr/bin/env python3
"""Local Modbus TCP virtual PLC for ClearVision development."""

from __future__ import annotations

import argparse
import signal
import threading
import time
from dataclasses import dataclass

from pymodbus.datastore import ModbusServerContext

try:
    from pymodbus import ModbusDeviceIdentification
except ImportError:  # pymodbus 3.6 compatibility
    from pymodbus.device import ModbusDeviceIdentification

try:
    from pymodbus.constants import ExcCodes
except ImportError:  # Older pymodbus versions only need normal-address responses here.
    ExcCodes = None

from pymodbus.server import StartTcpServer


COILS_SIZE = 128
REGISTERS_SIZE = 256

COIL_APP_HEARTBEAT = 0
COIL_PLC_HEARTBEAT = 1
COIL_PLC_READY = 2
COIL_START_REQ_VIEW = 10
COIL_START_ACK_VIEW = 11
COIL_DONE_VIEW = 12
COIL_ERROR_VIEW = 13

HR_COMMAND = 0
HR_STATUS = 1
HR_SEQUENCE = 2
HR_SEQUENCE_ECHO = 3
HR_ERROR_CODE = 4
HR_TEST_VALUE = 10

COMMAND_IDLE = 0
COMMAND_START = 1
COMMAND_RESET = 9

STATUS_IDLE = 0
STATUS_ACK_RUNNING = 100
STATUS_DONE = 200
STATUS_ERROR = 500

ERROR_COMMAND_REJECTED = 1001


class LockedDataBlock:
    """Simple sequential data block guarded for server and PLC-loop access."""

    def __init__(self, values: list[int] | list[bool], lock: threading.RLock):
        self._values = values
        self._lock = lock

    def getValues(self, address: int, count: int = 1):  # noqa: N802 - pymodbus API
        with self._lock:
            return list(self._values[address : address + count])

    def setValues(self, address: int, values):  # noqa: N802 - pymodbus API
        if not isinstance(values, list):
            values = [values]

        with self._lock:
            self._values[address : address + len(values)] = values

    def is_valid_range(self, address: int, count: int) -> bool:
        return address >= 0 and count >= 1 and address + count <= len(self._values)


@dataclass
class VirtualPlcBlocks:
    coils: LockedDataBlock
    discrete_inputs: LockedDataBlock
    holding_registers: LockedDataBlock
    input_registers: LockedDataBlock


class ClearVisionModbusServerContext(ModbusServerContext):
    """Pymodbus context backed by the virtual PLC memory arrays."""

    def __init__(self, unit_id: int, blocks: VirtualPlcBlocks) -> None:
        self.unit_id = unit_id
        self._blocks = blocks
        self.single = False
        self.simdevices = []
        self._devices = {unit_id: self}

    def device_ids(self) -> list[int]:
        return [self.unit_id]

    def slaves(self) -> list[int]:
        return self.device_ids()

    def __contains__(self, unit_id: int) -> bool:
        return unit_id in (0, self.unit_id)

    def __getitem__(self, unit_id: int):
        if unit_id not in self:
            raise KeyError(unit_id)
        return self

    async def async_getValues(  # noqa: N802 - pymodbus API
        self,
        device_id: int,
        func_code: int,
        address: int,
        count: int = 1,
    ):
        if device_id not in self or not self.validate(func_code, address, count):
            return illegal_address()
        return self.getValues(func_code, address, count)

    async def async_setValues(  # noqa: N802 - pymodbus API
        self,
        device_id: int,
        func_code: int,
        address: int,
        values: list[int] | list[bool],
    ):
        if device_id not in self or not self.validate(func_code, address, len(values)):
            return illegal_address()
        self.setValues(func_code, address, values)
        return None

    def validate(self, func_code: int, address: int, count: int = 1) -> bool:
        block = self._get_block(func_code)
        return block is not None and block.is_valid_range(address, count)

    def getValues(self, func_code: int, address: int, count: int = 1):  # noqa: N802 - pymodbus API
        block = self._get_block(func_code)
        if block is None:
            return []
        return block.getValues(address, count)

    def setValues(self, func_code: int, address: int, values):  # noqa: N802 - pymodbus API
        block = self._get_block(func_code)
        if block is not None:
            block.setValues(address, values)

    def _get_block(self, func_code: int) -> LockedDataBlock | None:
        if func_code in (1, 5, 15):
            return self._blocks.coils
        if func_code == 2:
            return self._blocks.discrete_inputs
        if func_code in (3, 6, 16, 22, 23):
            return self._blocks.holding_registers
        if func_code == 4:
            return self._blocks.input_registers
        return None


def illegal_address():
    if ExcCodes is None:
        return 2
    return ExcCodes.ILLEGAL_ADDRESS


class VirtualPlc:
    def __init__(
        self,
        blocks: VirtualPlcBlocks,
        cycle_ms: int,
        process_delay_ms: int,
        error_on_command: bool,
    ) -> None:
        self._blocks = blocks
        self._cycle_seconds = max(cycle_ms, 1) / 1000.0
        self._process_delay_seconds = max(process_delay_ms, 0) / 1000.0
        self._error_on_command = error_on_command
        self._running_since: float | None = None
        self._running_sequence: int = 0
        self._state = "idle"

    def initialize(self) -> None:
        self._write_coil(COIL_APP_HEARTBEAT, False)
        self._write_coil(COIL_PLC_HEARTBEAT, False)
        self._write_coil(COIL_PLC_READY, True)
        self._write_hr(HR_COMMAND, COMMAND_IDLE)
        self._write_hr(HR_STATUS, STATUS_IDLE)
        self._write_hr(HR_SEQUENCE, 0)
        self._write_hr(HR_SEQUENCE_ECHO, 0)
        self._write_hr(HR_ERROR_CODE, 0)
        self._write_hr(HR_TEST_VALUE, 1234)
        self._sync_view_coils()

    def run(self, stop_event: threading.Event) -> None:
        while not stop_event.is_set():
            self._scan_once(time.monotonic())
            stop_event.wait(self._cycle_seconds)

    def _scan_once(self, now: float) -> None:
        self._write_coil(COIL_PLC_HEARTBEAT, not self._read_coil(COIL_PLC_HEARTBEAT))
        self._write_coil(COIL_PLC_READY, True)

        command = self._read_hr(HR_COMMAND)
        status = self._read_hr(HR_STATUS)

        if command == COMMAND_RESET or (command == COMMAND_IDLE and status != STATUS_IDLE):
            self._reset(command)
        elif command == COMMAND_START:
            self._handle_start(now, status)

        self._sync_view_coils()

    def _handle_start(self, now: float, status: int) -> None:
        if self._error_on_command:
            if status != STATUS_ERROR:
                self._write_hr(HR_STATUS, STATUS_ERROR)
                self._write_hr(HR_ERROR_CODE, ERROR_COMMAND_REJECTED)
                print(
                    f"Virtual Modbus PLC command error: status={STATUS_ERROR}, "
                    f"error_code={ERROR_COMMAND_REJECTED}",
                    flush=True,
                )
            self._state = "error"
            return

        if self._state not in ("running", "done"):
            self._running_sequence = self._read_hr(HR_SEQUENCE)
            self._running_since = now
            self._state = "running"
            self._write_hr(HR_STATUS, STATUS_ACK_RUNNING)
            self._write_hr(HR_ERROR_CODE, 0)
            print(
                f"Virtual Modbus PLC handshake started: sequence={self._running_sequence}",
                flush=True,
            )

        if self._state == "running" and self._running_since is not None:
            if now - self._running_since >= self._process_delay_seconds:
                self._write_hr(HR_SEQUENCE_ECHO, self._running_sequence)
                self._write_hr(HR_STATUS, STATUS_DONE)
                self._state = "done"
                print(
                    f"Virtual Modbus PLC handshake done: sequence={self._running_sequence}",
                    flush=True,
                )

    def _reset(self, command: int) -> None:
        if self._state != "idle" or self._read_hr(HR_STATUS) != STATUS_IDLE or self._read_hr(HR_ERROR_CODE) != 0:
            print("Virtual Modbus PLC reset to idle.", flush=True)

        self._running_since = None
        self._running_sequence = 0
        self._state = "idle"
        self._write_hr(HR_STATUS, STATUS_IDLE)
        self._write_hr(HR_ERROR_CODE, 0)

        if command == COMMAND_RESET:
            self._write_hr(HR_COMMAND, COMMAND_IDLE)

    def _sync_view_coils(self) -> None:
        command = self._read_hr(HR_COMMAND)
        status = self._read_hr(HR_STATUS)

        self._write_coil(COIL_START_REQ_VIEW, command == COMMAND_START)
        self._write_coil(COIL_START_ACK_VIEW, status == STATUS_ACK_RUNNING)
        self._write_coil(COIL_DONE_VIEW, status == STATUS_DONE)
        self._write_coil(COIL_ERROR_VIEW, status == STATUS_ERROR)

    def _read_hr(self, address: int) -> int:
        return int(self._blocks.holding_registers.getValues(address, 1)[0])

    def _write_hr(self, address: int, value: int) -> None:
        self._blocks.holding_registers.setValues(address, [int(value) & 0xFFFF])

    def _read_coil(self, address: int) -> bool:
        return bool(self._blocks.coils.getValues(address, 1)[0])

    def _write_coil(self, address: int, value: bool) -> None:
        self._blocks.coils.setValues(address, [bool(value)])


def create_server_context(unit_id: int, blocks: VirtualPlcBlocks):
    return ClearVisionModbusServerContext(unit_id, blocks)


def create_identity() -> ModbusDeviceIdentification:
    identity = ModbusDeviceIdentification()
    identity.VendorName = "ClearVision"
    identity.ProductCode = "CVPLC"
    identity.VendorUrl = "https://example.local/clearvision"
    identity.ProductName = "ClearVision Virtual Modbus PLC"
    identity.ModelName = "Virtual Modbus TCP PLC"
    identity.MajorMinorRevision = "1.0"
    return identity


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run a ClearVision virtual Modbus TCP PLC.")
    parser.add_argument("--host", default="0.0.0.0", help="Listen address.")
    parser.add_argument("--port", type=int, default=1502, help="Listen port.")
    parser.add_argument("--unit-id", type=int, default=1, help="Modbus unit/slave id.")
    parser.add_argument("--cycle-ms", type=int, default=100, help="Virtual PLC scan cycle in milliseconds.")
    parser.add_argument("--process-delay-ms", type=int, default=500, help="Handshake processing delay in milliseconds.")
    parser.add_argument(
        "--error-on-command",
        type=int,
        choices=(0, 1),
        default=0,
        help="Set to 1 to return STATUS=500 and ERROR_CODE=1001 on start command.",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    lock = threading.RLock()
    blocks = VirtualPlcBlocks(
        coils=LockedDataBlock([False] * COILS_SIZE, lock),
        discrete_inputs=LockedDataBlock([False] * COILS_SIZE, lock),
        holding_registers=LockedDataBlock([0] * REGISTERS_SIZE, lock),
        input_registers=LockedDataBlock([0] * REGISTERS_SIZE, lock),
    )

    plc = VirtualPlc(
        blocks=blocks,
        cycle_ms=args.cycle_ms,
        process_delay_ms=args.process_delay_ms,
        error_on_command=args.error_on_command == 1,
    )
    plc.initialize()

    stop_event = threading.Event()
    signal.signal(signal.SIGINT, lambda _signum, _frame: stop_event.set())
    signal.signal(signal.SIGTERM, lambda _signum, _frame: stop_event.set())

    plc_thread = threading.Thread(target=plc.run, args=(stop_event,), name="virtual-plc-scan", daemon=True)
    plc_thread.start()

    print(
        f"Virtual Modbus PLC listening on {args.host}:{args.port}, unit id {args.unit_id}",
        flush=True,
    )

    context = create_server_context(args.unit_id, blocks)
    try:
        StartTcpServer(context=context, identity=create_identity(), address=(args.host, args.port))
    finally:
        stop_event.set()
        plc_thread.join(timeout=2)


if __name__ == "__main__":
    main()
