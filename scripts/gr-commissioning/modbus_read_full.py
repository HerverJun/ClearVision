import socket, struct

HOST = "172.16.87.12"
PORT = 502

def read(s, start, count):
    pdu = bytes([0, 1, 0, 0, 0, 6, 1, 3]) + struct.pack(">HH", start, count)
    s.sendall(pdu)
    s.settimeout(3.0)
    try:
        return s.recv(1024)
    except socket.timeout:
        return None

def regs_of(r):
    if r is None or r[7] != 0x03:
        return None
    n = r[8]
    d = r[9:9 + n]
    return [(d[i] << 8) | d[i + 1] for i in range(0, n, 2)]

def f(reg, i):
    u = (reg[i] << 16) | reg[i + 1]
    return struct.unpack(">f", struct.pack(">I", u))[0]

s = socket.socket()
s.settimeout(3.0)
try:
    s.connect((HOST, PORT))
except Exception as e:
    print("connect fail:", e)
    raise SystemExit
print("TCP connected ->", HOST, PORT)

# 先探一个区域确认是否生效
probe = read(s, 300, 12)
if probe is None:
    print("STILL SILENT -> Modbus 开关可能未真正生效(或 LAN2 没插网线触发了报警)")
    s.close()
    raise SystemExit

print("\n=== 各寄存器区域当前值 ===")

rg = regs_of(read(s, 0, 10))
if rg: print("user_define[0..9]      :", rg)

rg = regs_of(read(s, 300, 12))
if rg:
    x = [f(rg, i) for i in range(0, 12, 2)]
    print(f"cartesian XYZ/abc(mm,°) : X={x[0]:.3f} Y={x[1]:.3f} Z={x[2]:.3f} A={x[3]:.3f} B={x[4]:.3f} C={x[5]:.3f}")

rg = regs_of(read(s, 320, 12))
if rg:
    j = [f(rg, i) for i in range(0, 12, 2)]
    print("joint J1..J6 (deg)      :", [f"{v:.3f}" for v in j])

rg = regs_of(read(s, 437, 15))
if rg:
    names = ["poweron", "enable", "moving", "suspend", "stop", "error_num",
             "safe_door", "estop", "error", "start_trip", "door_enable",
             "clear", "robot_mode", "moving_mode", "hot_discon"]
    print("status[437..451]        :")
    for nm, v in zip(names, rg):
        print(f"    {nm:12s}: {v}")

rg = regs_of(read(s, 501, 1))
if rg: print("override (倍率 %)      :", rg[0])

rg = regs_of(read(s, 502, 4))
if rg: print("DO1,DO2,DI1,DI2        :", rg)

rg = regs_of(read(s, 601, 1))
if rg: print("yield (产量计数)       :", rg[0])

rg = regs_of(read(s, 1000, 11))
if rg:
    cn = ["overide", "enable", "unenable", "suspend", "start", "reset",
          "load1", "load2", "load3", "load4", "stop"]
    print("control[1000..1010]     :")
    for nm, v in zip(cn, rg):
        print(f"    {nm:10s}: {v}")

s.close()
print("\n=> done")
