import sys, re

try:
    from pdfminer.high_level import extract_text
except ImportError:
    import subprocess
    subprocess.check_call([sys.executable, "-m", "pip", "install", "pdfminer.six"])
    from pdfminer.high_level import extract_text

path = r"C:\Users\11234\Desktop\GR机器人通信总线手册 v3.0-1.pdf"
out = r"C:\Users\11234\Desktop\ClearVision\tmp\GR_pdf_fulltext.txt"

print("extracting...")
text = extract_text(path)
with open(out, "w", encoding="utf-8") as f:
    f.write(text)
print("total chars:", len(text))

print("\n=== SSH/Telnet/远程 occurrences (with context) ===")
for kw in ["SSH", "Telnet", "远程登录", "安全外壳", "shell"]:
    for m in re.finditer(re.escape(kw), text, re.IGNORECASE):
        s = max(0, m.start() - 90)
        e = min(len(text), m.end() + 90)
        print(f"\n--- [{kw}] @ {m.start()} ---")
        print(text[s:e].replace("\n", " "))
