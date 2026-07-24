paths = [
    r"C:\Users\11234\Desktop\ftp\robot-folder\MySystem\ModbusTCPapplmap.txt",
    r"C:\Users\11234\Desktop\ftp\robot-folder\MyProject\test1\MainFile.proc",
    r"C:\Users\11234\Desktop\ftp\robot-folder\MyProject\test1\test1.pro",
]
for p in paths:
    print("\n" + "=" * 72)
    print("FILE:", p)
    print("=" * 72)
    raw = open(p, "rb").read()
    txt = None
    for enc in ("utf-8", "gbk", "gb2312", "latin1"):
        try:
            txt = raw.decode(enc)
            print(f"[decoded OK with {enc}, {len(raw)} bytes]")
            break
        except Exception:
            pass
    if txt is None:
        txt = raw.decode("utf-8", "replace")
    print(txt)
