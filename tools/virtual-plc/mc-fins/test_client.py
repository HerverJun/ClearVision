#!/usr/bin/env python3
"""Smoke test client for the ClearVision virtual MC/FINS PLC."""

from __future__ import annotations

import argparse
import socket
import sys


def read_exact(sock: socket.socket, count: int) -> bytes:
    chunks: list[bytes] = []
    remaining = count
    while remaining > 0:
        chunk = sock.recv(remaining)
        if not chunk:
            raise RuntimeError("Connection closed while reading response.")
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks)


def build_mc_read_d0_request() -> bytes:
    payload = bytearray()
    payload += (0x0010).to_bytes(2, "little")
    payload += (0x0401).to_bytes(2, "little")
    payload += (0x0000).to_bytes(2, "little")
    payload += bytes([0x00, 0x00, 0x00])
    payload.append(0xA8)
    payload += (1).to_bytes(2, "little")

    frame = bytearray()
    frame += (0x0050).to_bytes(2, "little")
    frame += bytes([0x00, 0xFF])
    frame += (0x03FF).to_bytes(2, "little")
    frame.append(0x00)
    frame += len(payload).to_bytes(2, "little")
    frame += payload
    frame += b"\x00" * 6
    return bytes(frame)


def test_mc(host: str, port: int, timeout: float) -> None:
    with socket.create_connection((host, port), timeout=timeout) as sock:
        sock.settimeout(timeout)
        sock.sendall(build_mc_read_d0_request())
        header = read_exact(sock, 11)
        if int.from_bytes(header[0:2], "little") != 0x00D0:
            raise RuntimeError("MC response subheader was not 0x00D0.")
        data_length = int.from_bytes(header[7:9], "little")
        end_code = int.from_bytes(header[9:11], "little")
        payload = read_exact(sock, max(0, data_length - 2))
        if end_code != 0:
            raise RuntimeError(f"MC returned end code 0x{end_code:04X}.")
        if len(payload) < 2:
            raise RuntimeError("MC D0 read returned no word data.")
        value = int.from_bytes(payload[0:2], "little")
        if value != 0x1234:
            raise RuntimeError(f"MC expected D0=0x1234, got 0x{value:04X}.")


def fins_tcp_frame(command: int, payload: bytes, *, handshake_length: bool = False) -> bytes:
    length = len(payload) + 8 if handshake_length else len(payload)
    return b"FINS" + length.to_bytes(4, "big") + command.to_bytes(4, "big") + (0).to_bytes(4, "big") + payload


def test_fins(host: str, port: int, timeout: float) -> None:
    with socket.create_connection((host, port), timeout=timeout) as sock:
        sock.settimeout(timeout)
        sock.sendall(fins_tcp_frame(0, (0).to_bytes(4, "big"), handshake_length=True))
        handshake = read_exact(sock, 24)
        if handshake[0:4] != b"FINS":
            raise RuntimeError("FINS handshake magic was invalid.")
        if int.from_bytes(handshake[8:12], "big") != 1:
            raise RuntimeError("FINS handshake response command was not 1.")
        if int.from_bytes(handshake[12:16], "big") != 0:
            raise RuntimeError("FINS handshake returned an error.")
        server_node = int.from_bytes(handshake[16:20], "big") & 0xFF
        client_node = int.from_bytes(handshake[20:24], "big") & 0xFF

        fins_payload = bytearray()
        fins_payload += bytes([0x80, 0x00, 0x02, 0x00, server_node, 0x00, 0x00, client_node, 0x00, 0x01])
        fins_payload += bytes([0x01, 0x01, 0x82])
        fins_payload += (0).to_bytes(2, "big")
        fins_payload.append(0)
        fins_payload += (1).to_bytes(2, "big")

        sock.sendall(fins_tcp_frame(2, bytes(fins_payload)))
        header = read_exact(sock, 16)
        if header[0:4] != b"FINS":
            raise RuntimeError("FINS read response magic was invalid.")
        payload_length = int.from_bytes(header[4:8], "big")
        if int.from_bytes(header[8:12], "big") != 2:
            raise RuntimeError("FINS read response command was not 2.")
        if int.from_bytes(header[12:16], "big") != 0:
            raise RuntimeError("FINS/TCP read response returned an error.")
        response = read_exact(sock, payload_length - 8 if payload_length >= 8 else payload_length)
        if len(response) < 16:
            raise RuntimeError("FINS response payload was too short.")
        end_code = int.from_bytes(response[12:14], "big")
        if end_code != 0:
            raise RuntimeError(f"FINS returned end code 0x{end_code:04X}.")
        value = int.from_bytes(response[14:16], "big")
        if value != 0x1234:
            raise RuntimeError(f"FINS expected DM0=0x1234, got 0x{value:04X}.")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Smoke test the ClearVision virtual MC/FINS PLC.")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--mc-port", type=int, default=5002)
    parser.add_argument("--fins-port", type=int, default=9600)
    parser.add_argument("--timeout", type=float, default=5.0)
    parser.add_argument("--skip-mc", action="store_true")
    parser.add_argument("--skip-fins", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        if not args.skip_mc:
            test_mc(args.host, args.mc_port, args.timeout)
        if not args.skip_fins:
            test_fins(args.host, args.fins_port, args.timeout)
        print("Virtual MC/FINS PLC smoke test passed.")
        return 0
    except Exception as exc:
        print(f"Virtual MC/FINS PLC smoke test failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
