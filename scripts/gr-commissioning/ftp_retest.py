from ftplib import FTP

host = "172.16.87.12"

def try_login(user, pwd):
    f = FTP()
    f.connect(host, 21, timeout=5)
    f.set_pasv(True)
    try:
        f.login(user, pwd)
        return f
    except Exception as e:
        try: f.quit()
        except Exception: pass
        return ("LOGIN_FAIL", repr(e))

# 1) 纯匿名（不带密码 / 空密码 / 邮箱格式 三种都试）
f = None
for u, p in [("anonymous", ""), ("anonymous", "anonymous"), ("anonymous", "anonymous@local")]:
    r = try_login(u, p)
    if isinstance(r, FTP):
        print(f"anonymous login OK with user={u!r} pwd={p!r}")
        f = r
        break
    else:
        print(f"anonymous login FAIL {u!r}: {r[1]}")

if not f:
    print("NO anonymous access at all.")
else:
    print("\nroot nlst:", f.nlst())
    # 2) 相对路径逐级进入（不写绝对 /）
    for sub in ["robot-folder", "MyProject", "test1"]:
        try:
            f.cwd(sub)
            print(f"\n>>> cwd '{sub}' OK. contents:", f.nlst())
        except Exception as e:
            print(f"\n>>> cwd '{sub}' FAILED: {repr(e)}")
            break
    # 3) 尝试读取一个文件
    try:
        lines = []
        f.retrlines("RETR MainFile.proc", lines.append)
        print(f"\n>>> RETR MainFile.proc OK, {len(lines)} lines. first 25:")
        for l in lines[:25]:
            print("   |", l[:100])
    except Exception as e:
        print("\n>>> RETR MainFile.proc FAILED:", repr(e))
    try: f.quit()
    except Exception: pass
