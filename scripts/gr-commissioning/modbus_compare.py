# -*- coding: utf-8 -*-
import socket, struct, time

HOST = "172.16.87.12"
PORT = 502
UNIT = 1
TID = 0

# ---- 上次读取的基线值(2026-07-24 09:53 读取) ----
BASE = {
    "cart": [101.635, 806.494, 558.172, 89.197, 56.052, 88.150],   # X Y Z A B C
    "joint": [91.021, 15.697, 39.164, 89.642, -91.011, -1.210],    # J1-J6
}

def build_fc03(addr, count):
    global TID
    TID = (TID + 1) & 0xFFFF
    pdu = struct.pack(">B B H H", 0x01, 0x03, addr, count)
    req = struct.pack(">H H H B", TID, 0x0000, len(pdu) + 1, UNIT) + pdu
    return req

def read(s, addr, count):
    req = build_fc03(addr, count)
    s.sendall(req)
    # MBAP(7) + func(1) + nbytes(1) + regs(2*count)
    hdr = recv_exact(s, 7)
    _, _, _, _ = struct.unpack(">H H H B", hdr)
    func = recv_exact(s, 1)[0]
    if func == 0x83:
        err = recv_exact(s, 1)[0]
        raise RuntimeError("Modbus exception 0x%02x" % err)
    nbytes = recv_exact(s, 1)[0]
    raw = recv_exact(s, nbytes)
    return raw

def recv_exact(s, n):
    buf = b""
    while len(buf) < n:
        chunk = s.recv(n - len(buf))
        if not chunk:
            raise RuntimeError("connection closed")
        buf += chunk
    return buf

def regs_to_floats(raw):
    vals = []
    for i in range(0, len(raw), 4):
        a, b = struct.unpack(">HH", raw[i:i+4])
        vals.append(struct.unpack(">f", struct.pack(">HH", a, b))[0])
    return vals

def read_u16(s, addr, count):
    raw = read(s, addr, count)
    return list(struct.unpack(">%dH" % count, raw[:2*count]))

def status_decode(w):
    names = ["poweron","enable","moving","stop","error_num?","estop","?","safe_door",
             "alarm_ind","startkey","cabdoorkey","alarm_clear","?","robot_mode","moving_mode","teach_hotplug"]
    # 仅取已知位
    known = ["poweron","enable","moving","stop","estop","safe_door","alarm_ind","robot_mode","moving_mode","alarm_clear"]
    bits = {n: (w >> i) & 1 for i, n in enumerate(known)}
    err = w & 0x3F  # 低6位报警号
    bits["error_num"] = err
    return bits

def fmt_delta(cur, base):
    out = []
    for c, b in zip(cur, base):
        d = c - b
        if abs(d) < 1e-4:
            out.append("%.3f" % c)
        else:
            out.append("%.3f (%s%.3f)" % (c, "+" if d > 0 else "", d))
    return out

def connect_retry(retries=4):
    last = None
    for i in range(1, retries + 1):
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            s.settimeout(5)
            s.connect((HOST, PORT))
            return s
        except Exception as e:
            last = e
            print("  connect attempt %d failed: %s" % (i, e))
            time.sleep(1.5)
    raise RuntimeError("all connect attempts failed: %s" % last)

s = connect_retry()
print("TCP connected -> %s:%d\n" % (HOST, PORT))

def read_retry(s, addr, count, tries=3):
    for t in range(1, tries + 1):
        try:
            return read(s, addr, count)
        except Exception as e:
            if t == tries:
                raise
            print("  read addr=%d retry %d: %s" % (addr, t, e))
            time.sleep(1.2)

# 笛卡尔位姿 300..311 (6 float => 12 regs)
cart_raw = read_retry(s, 300, 12)
cart = regs_to_floats(cart_raw)
# 关节 320..331
joint_raw = read_retry(s, 320, 12)
joint = regs_to_floats(joint_raw)
# 状态字 437 (status word at 437)
status_raw = read_retry(s, 437, 1)
status_w = struct.unpack(">H", status_raw[:2])[0]
# 倍率 501
override = struct.unpack(">H", read_retry(s, 501, 1)[:2])[0]
# DI/DO 502..505
dio = list(struct.unpack(">4H", read_retry(s, 502, 4)[:8]))
# 产量 601
yield_c = struct.unpack(">H", read_retry(s, 601, 1)[:2])[0]

s.close()

print("=== 笛卡尔位姿 (mm / deg) ===")
labels = ["X","Y","Z","A","B","C"]
cd = fmt_delta(cart, BASE["cart"])
for i, l in enumerate(labels):
    print("  %s = %s" % (l, cd[i]))

print("\n=== 关节角 J1-J6 (deg) ===")
jl = ["J1","J2","J3","J4","J5","J6"]
jd = fmt_delta(joint, BASE["joint"])
for i, l in enumerate(jl):
    print("  %s = %s" % (l, jd[i]))

print("\n=== 状态字 (reg 437) ===")
sd = status_decode(status_w)
for k in ["poweron","enable","moving","stop","error_num","estop","safe_door","alarm_ind","robot_mode","moving_mode","alarm_clear"]:
    print("  %-12s = %d" % (k, sd[k]))

print("\n倍率(501) = %d%%" % override)
print("DO1/DO2/DI1/DI2(502-505) = %s" % list(dio))
print("产量(601) = %d" % yield_c)

# 变化总结
changed = any(abs(c - b) > 1e-4 for c, b in zip(cart, BASE["cart"])) or \
          any(abs(c - b) > 1e-4 for c, b in zip(joint, BASE["joint"]))
print("\n>>> 点位是否变化: %s" % ("有变化" if changed else "未变化(与基线一致)"))
