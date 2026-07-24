from ftplib import FTP

host = "172.16.87.12"
f = FTP()
f.connect(host, 21, timeout=4)
f.login("anonymous", "anonymous@local")
print("Logged in (anonymous). Top-level dirs/files already known.\n")

for d in ["robot-folder", "temp", "RobotLog", "BackupProject"]:
    try:
        f.cwd("/" + d)
        items = f.nlst()
        print(f"=== /{d}  ({len(items)} entries) ===")
        for x in items[:40]:
            print("   ", x)
        f.cwd("/")
    except Exception as e:
        print(f"=== /{d}  ACCESS FAILED: {e} ===")

# 真正读取一个文件内容（文本日志前 20 行）验证"可读文件"
print("\n--- try to actually READ a file (first 20 lines of a log) ---")
try:
    f.cwd("/RobotLog")
    logs = f.nlst()
    if logs:
        target = logs[0]
        lines = []
        def collect(line):
            lines.append(line)
        f.retrlines("RETR " + target, collect)
        print(f"File: /RobotLog/{target}  (read {len(lines)} lines)")
        for l in lines[:20]:
            print("   |", l[:120])
    else:
        print("/RobotLog is empty")
    f.cwd("/")
except Exception as e:
    print("file read test failed:", repr(e))

f.quit()
