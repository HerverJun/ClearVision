import socket
host = "172.16.87.12"
for p in (22, 502):
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.settimeout(2.0)
    try:
        r = s.connect_ex((host, p))
        print(f"Port {p} on {host}: {'OPEN' if r == 0 else 'CLOSED/TIMEOUT'}")
    except Exception as e:
        print(f"Port {p} on {host}: ERROR {e}")
    finally:
        s.close()
