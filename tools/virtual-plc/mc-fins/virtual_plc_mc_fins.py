#!/usr/bin/env python3
"""Local Mitsubishi MC and Omron FINS/TCP virtual PLC for ClearVision."""

from __future__ import annotations

import argparse
import signal
import socket
import socketserver
import struct
import threading
from dataclasses import dataclass, field


MC_REQUEST_SUBHEADER = 0x0050
MC_RESPONSE_SUBHEADER = 0x00D0
MC_BATCH_READ = 0x0401
MC_BATCH_WRITE = 0x1401
MC_WORD_ACCESS = 0x0000
MC_BIT_ACCESS = 0x0001
MC_END_OK = 0x0000
MC_END_UNSUPPORTED_COMMAND = 0xC059
MC_END_BAD_REQUEST = 0xC061

MC_DEVICE_D = 0xA8

FINS_MAGIC = b"FINS"
FINS_NODE_ADDRESS_REQUEST = 0
FINS_NODE_ADDRESS_RESPONSE = 1
FINS_FRAME_SEND = 2
FINS_ERROR_OK = 0
FINS_ICF_COMMAND = 0x80
FINS_ICF_RESPONSE = 0xC0
FINS_MRC_MEMORY_AREA = 0x01
FINS_SRC_MEMORY_READ = 0x01
FINS_SRC_MEMORY_WRITE = 0x02
FINS_END_OK = 0x0000
FINS_END_UNSUPPORTED_COMMAND = 0x1002
FINS_END_BAD_ADDRESS = 0x1103

FINS_AREA_DM_WORD = 0x82


@dataclass
class VirtualMcMemory:
    words: dict[tuple[int, int], int] = field(default_factory=dict)
    bits: dict[tuple[int, int], bool] = field(default_factory=dict)
    lock: threading.RLock = field(default_factory=threading.RLock)

    def __post_init__(self) -> None:
        self.words.setdefault((MC_DEVICE_D, 0), 0x1234)

    def read_words(self, device_code: int, start_address: int, count: int) -> list[int]:
        with self.lock:
            return [self.words.get((device_code, start_address + offset), 0) for offset in range(count)]

    def write_words(self, device_code: int, start_address: int, values: list[int]) -> None:
        with self.lock:
            for offset, value in enumerate(values):
                self.words[(device_code, start_address + offset)] = value & 0xFFFF

    def read_bits(self, device_code: int, start_address: int, count: int) -> list[bool]:
        with self.lock:
            return [self.bits.get((device_code, start_address + offset), False) for offset in range(count)]

    def write_bits(self, device_code: int, start_address: int, values: list[bool]) -> None:
        with self.lock:
            for offset, value in enumerate(values):
                self.bits[(device_code, start_address + offset)] = bool(value)


@dataclass
class VirtualFinsMemory:
    words: dict[tuple[int, int], int] = field(default_factory=dict)
    bits: dict[tuple[int, int, int], bool] = field(default_factory=dict)
    lock: threading.RLock = field(default_factory=threading.RLock)

    def __post_init__(self) -> None:
        self.words.setdefault((FINS_AREA_DM_WORD, 0), 0x1234)

    def read_words(self, area_code: int, start_address: int, count: int) -> list[int]:
        with self.lock:
            return [self.words.get((area_code, start_address + offset), 0) for offset in range(count)]

    def write_words(self, area_code: int, start_address: int, values: list[int]) -> None:
        with self.lock:
            for offset, value in enumerate(values):
                self.words[(area_code, start_address + offset)] = value & 0xFFFF

    def read_bits(self, area_code: int, start_address: int, bit_address: int, count: int) -> list[bool]:
        with self.lock:
            return [
                self.bits.get((area_code, start_address + ((bit_address + offset) // 16), (bit_address + offset) % 16), False)
                for offset in range(count)
            ]

    def write_bits(self, area_code: int, start_address: int, bit_address: int, values: list[bool]) -> None:
        with self.lock:
            for offset, value in enumerate(values):
                absolute_bit = bit_address + offset
                self.bits[(area_code, start_address + (absolute_bit // 16), absolute_bit % 16)] = bool(value)


def read_exact(sock: socket.socket, count: int) -> bytes:
    chunks: list[bytes] = []
    remaining = count
    while remaining > 0:
        chunk = sock.recv(remaining)
        if not chunk:
            raise ConnectionError("connection closed")
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks)


def try_drain_zero_padding(sock: socket.socket) -> None:
    previous_timeout = sock.gettimeout()
    try:
        sock.settimeout(0.02)
        peek_flags = getattr(socket, "MSG_PEEK", 0)
        while True:
            try:
                peeked = sock.recv(64, peek_flags)
            except socket.timeout:
                return
            if not peeked or any(byte != 0 for byte in peeked):
                return
            sock.recv(len(peeked))
    finally:
        sock.settimeout(previous_timeout)


class ThreadedTcpServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


class MitsubishiMcHandler(socketserver.BaseRequestHandler):
    def handle(self) -> None:
        memory: VirtualMcMemory = self.server.memory  # type: ignore[attr-defined]
        self.request.settimeout(5)
        while True:
            try:
                header = read_exact(self.request, 9)
                data_length = int.from_bytes(header[7:9], "little")
                payload = read_exact(self.request, data_length)
                try_drain_zero_padding(self.request)
                response = handle_mc_request(memory, header, payload)
                self.request.sendall(response)
            except (ConnectionError, OSError, socket.timeout):
                return


def handle_mc_request(memory: VirtualMcMemory, header: bytes, payload: bytes) -> bytes:
    if len(header) != 9 or len(payload) < 12:
        return build_mc_response(header, b"", MC_END_BAD_REQUEST)

    subheader = int.from_bytes(header[0:2], "little")
    if subheader != MC_REQUEST_SUBHEADER:
        return build_mc_response(header, b"", MC_END_BAD_REQUEST)

    command = int.from_bytes(payload[2:4], "little")
    subcommand = int.from_bytes(payload[4:6], "little")
    start_address = payload[6] | (payload[7] << 8) | (payload[8] << 16)
    device_code = payload[9]
    count = int.from_bytes(payload[10:12], "little")
    is_bit_access = subcommand == MC_BIT_ACCESS

    if command == MC_BATCH_READ:
        if is_bit_access:
            bits = memory.read_bits(device_code, start_address, count)
            data = bytes(1 if bit else 0 for bit in bits)
        else:
            data = b"".join(value.to_bytes(2, "little") for value in memory.read_words(device_code, start_address, count))
        print(f"MC read: device=0x{device_code:02X}, address={start_address}, count={count}", flush=True)
        return build_mc_response(header, data, MC_END_OK)

    if command == MC_BATCH_WRITE:
        write_data = payload[12:]
        if is_bit_access:
            memory.write_bits(device_code, start_address, [byte != 0 for byte in write_data[:count]])
        else:
            values = [
                int.from_bytes(write_data[index : index + 2].ljust(2, b"\x00"), "little")
                for index in range(0, min(len(write_data), count * 2), 2)
            ]
            memory.write_words(device_code, start_address, values)
        print(f"MC write: device=0x{device_code:02X}, address={start_address}, count={count}", flush=True)
        return build_mc_response(header, b"", MC_END_OK)

    return build_mc_response(header, b"", MC_END_UNSUPPORTED_COMMAND)


def build_mc_response(request_header: bytes, data: bytes, end_code: int) -> bytes:
    network = request_header[2] if len(request_header) > 2 else 0x00
    pc_number = request_header[3] if len(request_header) > 3 else 0xFF
    module_io = request_header[4:6] if len(request_header) > 5 else b"\xFF\x03"
    station = request_header[6] if len(request_header) > 6 else 0x00
    response = bytearray()
    response += MC_RESPONSE_SUBHEADER.to_bytes(2, "little")
    response.append(network)
    response.append(pc_number)
    response += module_io
    response.append(station)
    response += (2 + len(data)).to_bytes(2, "little")
    response += end_code.to_bytes(2, "little")
    response += data
    return bytes(response)


class OmronFinsHandler(socketserver.BaseRequestHandler):
    def handle(self) -> None:
        memory: VirtualFinsMemory = self.server.memory  # type: ignore[attr-defined]
        server_node: int = self.server.server_node  # type: ignore[attr-defined]
        default_client_node: int = self.server.client_node  # type: ignore[attr-defined]
        self.request.settimeout(5)
        while True:
            try:
                header = read_exact(self.request, 16)
                if header[0:4] != FINS_MAGIC:
                    return
                length = int.from_bytes(header[4:8], "big")
                command = int.from_bytes(header[8:12], "big")
                if command == FINS_NODE_ADDRESS_REQUEST:
                    payload = read_exact(self.request, max(0, length - 8))
                    requested_client = int.from_bytes(payload[0:4].ljust(4, b"\x00"), "big") if payload else 0
                    client_node = requested_client or default_client_node
                    print(f"FINS handshake: client_node={client_node}, server_node={server_node}", flush=True)
                    self.request.sendall(build_fins_node_response(server_node, client_node))
                elif command == FINS_FRAME_SEND:
                    payload_length = resolve_fins_payload_length(length)
                    payload = read_exact(self.request, payload_length)
                    response = handle_fins_frame(memory, payload, server_node)
                    self.request.sendall(response)
                else:
                    self.request.sendall(build_fins_tcp_header(b"", command, error_code=1))
            except (ConnectionError, OSError, socket.timeout):
                return


def build_fins_node_response(server_node: int, client_node: int) -> bytes:
    response = bytearray()
    response += FINS_MAGIC
    response += (16).to_bytes(4, "big")
    response += FINS_NODE_ADDRESS_RESPONSE.to_bytes(4, "big")
    response += FINS_ERROR_OK.to_bytes(4, "big")
    response += server_node.to_bytes(4, "big")
    response += client_node.to_bytes(4, "big")
    return bytes(response)


def handle_fins_frame(memory: VirtualFinsMemory, frame: bytes, server_node: int) -> bytes:
    if len(frame) < 18:
        return build_fins_response(frame, server_node, FINS_MRC_MEMORY_AREA, FINS_SRC_MEMORY_READ, FINS_END_BAD_ADDRESS)

    sid = frame[9]
    client_node = frame[7]
    mrc = frame[10]
    src = frame[11]
    area_code = frame[12]
    start_address = int.from_bytes(frame[13:15], "big")
    bit_address = frame[15]
    count = int.from_bytes(frame[16:18], "big")
    is_bit_access = is_fins_bit_area(area_code)

    if mrc != FINS_MRC_MEMORY_AREA or src not in (FINS_SRC_MEMORY_READ, FINS_SRC_MEMORY_WRITE):
        return build_fins_response(frame, server_node, mrc, src, FINS_END_UNSUPPORTED_COMMAND)

    if src == FINS_SRC_MEMORY_READ:
        if is_bit_access:
            data = bytes(1 if bit else 0 for bit in memory.read_bits(area_code, start_address, bit_address, count))
        else:
            data = b"".join(value.to_bytes(2, "big") for value in memory.read_words(area_code, start_address, count))
        print(f"FINS read: area=0x{area_code:02X}, address={start_address}, count={count}", flush=True)
        return build_fins_response(frame, server_node, mrc, src, FINS_END_OK, data, client_node=client_node, sid=sid)

    write_data = frame[18:]
    if is_bit_access:
        memory.write_bits(area_code, start_address, bit_address, [byte != 0 for byte in write_data[:count]])
    else:
        values = [
            int.from_bytes(write_data[index : index + 2].ljust(2, b"\x00"), "big")
            for index in range(0, min(len(write_data), count * 2), 2)
        ]
        memory.write_words(area_code, start_address, values)
    print(f"FINS write: area=0x{area_code:02X}, address={start_address}, count={count}", flush=True)
    return build_fins_response(frame, server_node, mrc, src, FINS_END_OK, client_node=client_node, sid=sid)


def resolve_fins_payload_length(length: int) -> int:
    if length < 18:
        return length

    if length - 8 >= 18:
        return length - 8

    return length


def is_fins_bit_area(area_code: int) -> bool:
    return area_code in {0x02, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33}


def build_fins_response(
    request_frame: bytes,
    server_node: int,
    mrc: int,
    src: int,
    end_code: int,
    data: bytes = b"",
    *,
    client_node: int | None = None,
    sid: int | None = None,
) -> bytes:
    if client_node is None:
        client_node = request_frame[7] if len(request_frame) > 7 else 0x02
    if sid is None:
        sid = request_frame[9] if len(request_frame) > 9 else 0x00

    fins_frame = bytearray()
    fins_frame += bytes([FINS_ICF_RESPONSE, 0x00, 0x02, 0x00, client_node, 0x00, 0x00, server_node, 0x00, sid])
    fins_frame += bytes([mrc, src])
    fins_frame += end_code.to_bytes(2, "big")
    fins_frame += data
    return build_fins_tcp_header(bytes(fins_frame), FINS_FRAME_SEND)


def build_fins_tcp_header(payload: bytes, command: int, error_code: int = FINS_ERROR_OK) -> bytes:
    frame = bytearray()
    frame += FINS_MAGIC
    frame += len(payload).to_bytes(4, "big")
    frame += command.to_bytes(4, "big")
    frame += error_code.to_bytes(4, "big")
    frame += payload
    return bytes(frame)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run ClearVision virtual Mitsubishi MC and Omron FINS/TCP PLC servers.")
    parser.add_argument("--host", default="0.0.0.0", help="Listen address for both servers.")
    parser.add_argument("--mc-port", type=int, default=5002, help="Mitsubishi MC TCP port.")
    parser.add_argument("--fins-port", type=int, default=9600, help="Omron FINS/TCP port.")
    parser.add_argument("--fins-server-node", type=int, default=1, help="FINS server node number.")
    parser.add_argument("--fins-client-node", type=int, default=2, help="FINS client node number returned when auto-assigned.")
    parser.add_argument("--disable-mc", action="store_true", help="Do not start the Mitsubishi MC server.")
    parser.add_argument("--disable-fins", action="store_true", help="Do not start the Omron FINS/TCP server.")
    return parser.parse_args()


def start_server(server: ThreadedTcpServer, name: str) -> threading.Thread:
    thread = threading.Thread(target=server.serve_forever, name=name, daemon=True)
    thread.start()
    return thread


def main() -> None:
    args = parse_args()
    if args.disable_mc and args.disable_fins:
        raise SystemExit("At least one server must be enabled.")

    servers: list[ThreadedTcpServer] = []
    threads: list[threading.Thread] = []
    stop_event = threading.Event()

    signal.signal(signal.SIGINT, lambda _signum, _frame: stop_event.set())
    signal.signal(signal.SIGTERM, lambda _signum, _frame: stop_event.set())

    if not args.disable_mc:
        mc_server = ThreadedTcpServer((args.host, args.mc_port), MitsubishiMcHandler)
        mc_server.memory = VirtualMcMemory()  # type: ignore[attr-defined]
        servers.append(mc_server)
        threads.append(start_server(mc_server, "virtual-mc-plc"))
        print(f"Virtual Mitsubishi MC PLC listening on {args.host}:{args.mc_port}", flush=True)

    if not args.disable_fins:
        fins_server = ThreadedTcpServer((args.host, args.fins_port), OmronFinsHandler)
        fins_server.memory = VirtualFinsMemory()  # type: ignore[attr-defined]
        fins_server.server_node = args.fins_server_node & 0xFF  # type: ignore[attr-defined]
        fins_server.client_node = args.fins_client_node & 0xFF  # type: ignore[attr-defined]
        servers.append(fins_server)
        threads.append(start_server(fins_server, "virtual-fins-plc"))
        print(f"Virtual Omron FINS/TCP PLC listening on {args.host}:{args.fins_port}", flush=True)

    try:
        while not stop_event.wait(0.2):
            pass
    finally:
        for server in servers:
            server.shutdown()
            server.server_close()
        for thread in threads:
            thread.join(timeout=2)


if __name__ == "__main__":
    main()
