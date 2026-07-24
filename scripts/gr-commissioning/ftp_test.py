import socket
from ftplib import FTP

host = "172.16.87.12"

def probe(p):
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.settimeout(2.0)
    try:
        r = s.connect_ex((host, p))
        return "OPEN" if r == 0 else "CLOSED/TIMEOUT"
    except Exception as e:
        return f"ERR {e}"
    finally:
        s.close()

print(f"Port 21 on {host}:", probe(21))

if probe(21) == "OPEN":
    # 1) 匿名登录尝试
    tried = False
    try:
        f = FTP()
        f.connect(host, 21, timeout=4)
        print("Banner:", f.getwelcome())
        try:
            f.login("anonymous", "anonymous@local")
            print("ANONYMOUS login: OK")
            tried = True
        except Exception as e:
            print("ANONYMOUS login: FAILED ->", repr(e))
        if tried:
            try:
                files = f.nlst()
                print(f"Root listing ({len(files)} items), first 60:")
                for x in files[:60]:
                    print("  ", x)
            except Exception as e:
                print("nlst failed:", repr(e))
            try:
                f.quit()
            except Exception:
                pass
    except Exception as e:
        print("FTP connect failed:", repr(e))
    print("\nNOTE: FTP is a generic service not documented in the GR comms manual; "
          "if anonymous is rejected, a vendor/teach-pendant FTP account is required.")
else:
    print("FTP port not open -> controller does not expose FTP (like SSH on :22). "
          "Only ModbusTCP :502 is confirmed open.")
