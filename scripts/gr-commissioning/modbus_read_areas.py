import socket, struct

HOST = "172.16.87.12"
PORT = 502

# (label, start_addr, count)  -- 地址直接用映射表里的保持寄存器编号
areas = [
    ("user_define[0..9]",        0,   10),
    ("cart_pos X[300..311]",    300, 12),
    ("joint[320..331]",         320, 12),
    ("status[437..451]",        437, 15),
    ("override[501]",           501, 1),
    ("DO1/DI1[502..505]",       502, 4),
    ("yield[601]",              601, 1),
    ("control[1000..1010]",     1000, 11),
]

def read_area(s, start, count):
    tid = 0x0001
    pdu = bytes([tid >> 8, tid & 0xFF, 0x00, 0x00, 0x00, 0x06, 0x01, 0x03]) \
        + struct.pack(">HH", start, count)
    s.sendall(pdu)
    s.settimeout(3.0)
    try:
        return s.recv(1024)
    except socket.timeout:
        return None

s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
s.settimeout(3.0)
try:
    s.connect((HOST, PORT))
except Exception as e:
    print("TCP connect FAILED:", e)
    raise SystemExit

print(f"TCP connected -> {HOST}:{PORT}\n")

any_resp = False
for label, start, count in areas:
    r = read_area(s, start, count)
    if r is None:
        print(f"[NO RESP]  {label:24s} addr={start} count={count}  (静默)")
        continue
    any_resp = True
    func = r[7]
    if func == 0x03:
        nbytes = r[8]
        data = r[9:9 + nbytes]
        regs = [(data[i] << 8) | data[i + 1] for i in range(0, nbytes, 2)]
        print(f"[OK]       {label:24s} regs({len(regs)})={regs}")
    elif func & 0x80:
        print(f"[EXC]      {label:24s} Modbus exception code={r[8]}")
    else:
        print(f"[?]        {label:24s} raw={r.hex()}")

s.close()
print("\n=> any response:", any_resp)
if not any_resp:
    print("=> 全部静默: 最可能是示教器 Modbus 开关未开(默认关闭)。")
