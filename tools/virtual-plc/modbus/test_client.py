#!/usr/bin/env python3
"""Smoke test client for the ClearVision virtual Modbus TCP PLC."""

from __future__ import annotations

import argparse
import sys
import time
from typing import Any, Callable

from pymodbus.client import ModbusTcpClient


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Smoke test the ClearVision virtual Modbus TCP PLC.")
    parser.add_argument("--host", default="127.0.0.1", help="Server host.")
    parser.add_argument("--port", type=int, default=1502, help="Server port.")
    parser.add_argument("--unit-id", type=int, default=1, help="Modbus unit/slave id.")
    parser.add_argument("--timeout", type=float, default=5.0, help="Timeout in seconds.")
    return parser.parse_args()


def call_modbus(method: Callable[..., Any], *args: Any, unit_id: int, **kwargs: Any) -> Any:
    last_error: TypeError | None = None
    for unit_keyword in ("device_id", "slave", "unit"):
        try:
            return method(*args, **kwargs, **{unit_keyword: unit_id})
        except TypeError as exc:
            last_error = exc

    try:
        return method(*args, **kwargs)
    except TypeError:
        if last_error is not None:
            raise last_error
        raise


def ensure_ok(response: Any, action: str) -> Any:
    if response is None:
        raise RuntimeError(f"{action} returned no response.")

    is_error = getattr(response, "isError", None)
    if callable(is_error) and is_error():
        raise RuntimeError(f"{action} failed: {response}")

    return response


def read_coil(client: ModbusTcpClient, address: int, unit_id: int) -> bool:
    response = ensure_ok(
        call_modbus(client.read_coils, address, count=1, unit_id=unit_id),
        f"Read coil {address}",
    )
    bits = getattr(response, "bits", None)
    if not bits:
        raise RuntimeError(f"Read coil {address} returned no bits.")
    return bool(bits[0])


def read_hr(client: ModbusTcpClient, address: int, unit_id: int) -> int:
    response = ensure_ok(
        call_modbus(client.read_holding_registers, address, count=1, unit_id=unit_id),
        f"Read HR{address}",
    )
    registers = getattr(response, "registers", None)
    if not registers:
        raise RuntimeError(f"Read HR{address} returned no registers.")
    return int(registers[0])


def write_hr(client: ModbusTcpClient, address: int, value: int, unit_id: int) -> None:
    ensure_ok(
        call_modbus(client.write_register, address, value, unit_id=unit_id),
        f"Write HR{address}={value}",
    )


def wait_for_hr(
    client: ModbusTcpClient,
    address: int,
    expected: int,
    unit_id: int,
    timeout: float,
) -> None:
    deadline = time.monotonic() + timeout
    last_value: int | None = None

    while time.monotonic() < deadline:
        last_value = read_hr(client, address, unit_id)
        if last_value == expected:
            return
        if address == 1 and last_value == 500:
            error_code = read_hr(client, 4, unit_id)
            raise RuntimeError(f"Handshake entered error status 500, error code {error_code}.")
        time.sleep(0.1)

    raise RuntimeError(f"Timed out waiting for HR{address}={expected}; last value was {last_value}.")


def run_smoke_test(args: argparse.Namespace) -> None:
    client = ModbusTcpClient(args.host, port=args.port, timeout=args.timeout)
    try:
        if not client.connect():
            raise RuntimeError(f"Could not connect to {args.host}:{args.port}.")

        ready = read_coil(client, 2, args.unit_id)
        if not ready:
            raise RuntimeError("Coil 2 PLC_READY was false.")

        initial_test_value = read_hr(client, 10, args.unit_id)
        if initial_test_value != 1234:
            raise RuntimeError(f"Expected HR10 initial value 1234, got {initial_test_value}.")

        write_hr(client, 10, 5678, args.unit_id)
        updated_test_value = read_hr(client, 10, args.unit_id)
        if updated_test_value != 5678:
            raise RuntimeError(f"Expected HR10 updated value 5678, got {updated_test_value}.")

        write_hr(client, 2, 123, args.unit_id)
        write_hr(client, 0, 1, args.unit_id)
        wait_for_hr(client, 1, 200, args.unit_id, args.timeout)

        sequence_echo = read_hr(client, 3, args.unit_id)
        if sequence_echo != 123:
            raise RuntimeError(f"Expected HR3 sequence echo 123, got {sequence_echo}.")

        write_hr(client, 0, 9, args.unit_id)
        wait_for_hr(client, 1, 0, args.unit_id, args.timeout)

        print("Virtual Modbus PLC smoke test passed.")
    finally:
        client.close()


def main() -> int:
    args = parse_args()
    try:
        run_smoke_test(args)
        return 0
    except Exception as exc:
        print(f"Virtual Modbus PLC smoke test failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
