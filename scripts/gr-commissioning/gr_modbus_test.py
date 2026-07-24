# -*- coding: utf-8 -*-
"""GR 机械臂 ModbusTCP 联机测试 — 单连接版(控制器只支持 1 个客户端)"""
import sys, struct, socket, binascii, time
sys.stdout.reconfigure(encoding='utf-8')

IP, PORT, UID = '172.16.87.12', 502, 1
TID = 0

def connect(retries=6, wait=5):
    for i in range(retries):
        try:
            s = socket.socket()
            s.settimeout(3)
            s.connect((IP, PORT))
            print(f'[TCP] {IP}:{PORT} 已连接')
            return s
        except Exception as e:
            print(f'[TCP] 第{i+1}次连接失败({e}),{wait}s 后重试...')
            time.sleep(wait)
    return None

def read_regs(s, addr, count):
    """FC03 读保持寄存器,返回 list[int] 或 None"""
    global TID
    TID = (TID + 1) & 0xFFFF
    req = struct.pack('>HHHBBHH', TID, 0, 6, UID, 3, addr, count)
    s.send(req)
    try:
        resp = s.recv(512)
    except socket.timeout:
        return None
    if len(resp) < 9:
        print(f'  [!] 响应过短: {binascii.hexlify(resp).decode()}')
        return None
    fc = resp[7]
    if fc == 0x83:
        print(f'  [!] 异常码: {resp[8]}')
        return None
    n = resp[8]
    return list(struct.unpack(f'>{n//2}H', resp[9:9+n]))

def f32(hi, lo):
    return struct.unpack('>f', struct.pack('>HH', hi, lo))[0]

s = connect()
if not s:
    print('[失败] 无法建立 TCP 连接(可能被其它客户端占用)'); sys.exit(1)

ok = False

r = read_regs(s, 501, 1)
if r is not None:
    print(f'\n[501] 速度倍率 = {r[0]} %'); ok = True
else:
    print('\n[501] 无响应')

r = read_regs(s, 320, 12)
if r:
    ok = True
    print('\n关节角:')
    for i in range(6):
        print(f'  J{i+1} = {f32(r[i*2], r[i*2+1]):10.3f} °')

r = read_regs(s, 300, 12)
if r:
    ok = True
    names = ['X(mm)','Y(mm)','Z(mm)','a(°)','b(°)','c(°)']
    print('\n笛卡尔坐标:')
    for i in range(6):
        print(f'  {names[i]:6s} = {f32(r[i*2], r[i*2+1]):10.3f}')

r = read_regs(s, 437, 14)
if r:
    ok = True
    labels = {0:'系统通电',1:'使能',2:'运行中',3:'暂停',4:'停止',5:'报警码',
              6:'安全门',7:'急停',8:'报警状态',12:'机器人模式',13:'运行模式'}
    print('\n状态字:')
    for idx, name in labels.items():
        print(f'  [{437+idx}] {name} = {r[idx]}')

s.close()
print('\n[结果]', '通讯成功!' if ok else 'TCP 通但 Modbus 无响应(检查示教器 Modbus 开关)')
