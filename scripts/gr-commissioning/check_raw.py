data = open(r"C:\Users\11234\Desktop\GR机器人通信总线手册 v3.0-1.pdf", "rb").read()
print("PDF size:", len(data))
for pat in [b"SSH", b"ssh", b"Telnet", b"telnet", b"shell", b"Shell"]:
    idx = 0
    cnt = 0
    while True:
        i = data.find(pat, idx)
        if i < 0:
            break
        ctx = data[max(0, i - 70):i + 70]
        # keep only printable-ish bytes for readability
        printable = bytes(b if 32 <= b < 127 else 46 for b in ctx)
        print(f"\n[{pat.decode('latin1')}] @ byte {i}:")
        print(printable.decode("latin1"))
        cnt += 1
        idx = i + 1
        if cnt >= 5:
            break
    if cnt == 0:
        print(f"[{pat.decode('latin1')}] not found")
