#!/usr/bin/env python3
"""Sequential driver for the Modelica-aware VCS tools. Usage: vcs_test.py <exe> <repo_path>"""
import json, subprocess, sys, threading, time

exe, repo = sys.argv[1], sys.argv[2]
proc = subprocess.Popen(exe, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
responses = {}

def reader():
    for line in proc.stdout:
        line = line.strip()
        if not line:
            continue
        try:
            msg = json.loads(line)
        except Exception:
            continue
        if "id" in msg:
            responses[msg["id"]] = msg

threading.Thread(target=reader, daemon=True).start()

_id = 1
def send(method, params, wait=True):
    global _id
    msg = {"jsonrpc": "2.0", "method": method}
    mid = None
    if method != "notifications/initialized":
        mid = _id; msg["id"] = mid; _id += 1
    if params is not None:
        msg["params"] = params
    proc.stdin.write(json.dumps(msg) + "\n"); proc.stdin.flush()
    if wait and mid is not None:
        deadline = time.time() + 40
        while mid not in responses and time.time() < deadline:
            time.sleep(0.05)
    return mid

def payload(mid):
    msg = responses.get(mid, {})
    r = msg.get("result", {})
    c = r.get("content")
    if c and isinstance(c, list) and "text" in c[0]:
        try:
            return json.loads(c[0]["text"])
        except Exception:
            return c[0]["text"]
    return r or msg.get("error")

def call(name, args):
    return payload(send("tools/call", {"name": name, "arguments": args}))

send("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "t", "version": "1"}})
send("notifications/initialized", None)

out = {}
load = call("load_repository", {"path": repo})
out["load_repository"] = load
rid = load.get("repositoryId")
out["get_changed_classes (working copy)"] = call("get_changed_classes", {"repositoryId": rid})
out["analyze_change_impact (pre-analyze)"] = call("analyze_change_impact", {"repositoryId": rid})
out["analyze_dependencies"] = call("analyze_dependencies", {})
out["analyze_change_impact (working copy)"] = call("analyze_change_impact", {"repositoryId": rid})
out["get_changed_classes (revision HEAD)"] = call("get_changed_classes", {"repositoryId": rid, "revision": "HEAD"})

proc.stdin.close()
try:
    proc.wait(timeout=10)
except Exception:
    proc.kill()

for k, v in out.items():
    print(f"--- {k} ---")
    print(json.dumps(v, indent=1)[:1500])
