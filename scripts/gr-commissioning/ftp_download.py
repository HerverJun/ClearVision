import os, io, time
from ftplib import FTP

HOST = "172.16.87.12"
BASE = r"C:\Users\11234\Desktop\ftp"
MAX_MB = 20
SKIP_EXT = (".tar", ".gz", ".zip", ".7z", ".rar", ".iso")

os.makedirs(BASE, exist_ok=True)

f = FTP()
connected = False
for attempt in range(4):
    try:
        f.connect(HOST, 21, timeout=15)
        connected = True
        break
    except Exception as e:
        print(f"connect attempt {attempt+1} failed: {e}; retry in 3s")
        time.sleep(3)
if not connected:
    raise SystemExit("cannot connect to FTP after retries (close Explorer FTP window?)")
f.set_pasv(True)
f.login("anonymous", "")   # 空密码, 相对路径
print("anon login OK")

# ---------- 验证可写 ----------
print("\n=== WRITE TEST (anon) ===")
writable = False
for label, fn in [("MKD/RMD", None), ("STOR/DELE", None)]:
    pass
try:
    f.mkd("__write_test__"); f.rmd("__write_test__")
    print("[OK] MKD/RMD succeeded -> writable"); writable = True
except Exception as e:
    print(f"[NO] MKD failed: {e}")
try:
    f.storbinary("STOR __write_test__.txt", io.BytesIO(b"hello"))
    f.delete("__write_test__.txt")
    print("[OK] STOR/DELE succeeded -> writable"); writable = True
except Exception as e:
    print(f"[NO] STOR failed: {e}")

# ---------- 递归下载 ----------
print("\n=== DOWNLOAD ===")
st = {"files": 0, "bytes": 0, "skip": 0, "err": 0}

def size_of(name):
    try:
        return f.size(name)
    except Exception:
        return None

def walk(rel, local):
    if rel:
        f.cwd(rel)
    for n in f.nlst():
        is_dir = False
        try:
            f.cwd(n); is_dir = True
        except Exception:
            pass
        if is_dir:
            f.cwd("..")
            sub = os.path.join(local, n)
            os.makedirs(sub, exist_ok=True)
            walk(n, sub)
        else:
            low = n.lower()
            if low.endswith(SKIP_EXT):
                print(f"[SKIP ext] {rel}/{n}"); st["skip"] += 1; continue
            sz = size_of(n)
            if sz is not None and sz > MAX_MB * 1024 * 1024:
                print(f"[SKIP {sz//1024//1024}MB] {rel}/{n}"); st["skip"] += 1; continue
            dst = os.path.join(local, n)
            try:
                with open(dst, "wb") as fp:
                    f.retrbinary("RETR " + n, fp.write)
                got = os.path.getsize(dst)
                st["files"] += 1; st["bytes"] += got
                print(f"[OK {got}B] {rel}/{n}")
            except Exception as e:
                print(f"[ERR] {rel}/{n}: {e}"); st["err"] += 1
    if rel:
        f.cwd("..")

t0 = time.time()
walk("", BASE)
dt = time.time() - t0
print(f"\n=== DONE in {dt:.1f}s ===")
print(f"files={st['files']} bytes={st['bytes']} skipped={st['skip']} errors={st['err']}")
print(f"anon writable = {writable}")
f.quit()
