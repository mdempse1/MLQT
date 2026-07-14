#!/usr/bin/env python3
"""Sequential driver for the dependency/impact/resource group. Usage: dep_test.py <exe> <load_dir>"""
import json, subprocess, sys, threading, time

exe, load_dir = sys.argv[1], sys.argv[2]
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

def call(name, args):
    return send("tools/call", {"name": name, "arguments": args})

send("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "t", "version": "1"}})
send("notifications/initialized", None)
labels = {}
labels[call("load_library", {"path": load_dir})] = "load_library"
labels[call("get_dependencies", {"classId": "TestLib.Top"})] = "get_dependencies Top (pre-analyze)"
labels[call("analyze_dependencies", {})] = "analyze_dependencies"
labels[call("get_dependencies", {"classId": "TestLib.Top"})] = "get_dependencies Top"
labels[call("get_dependencies", {"classId": "TestLib.Middle"})] = "get_dependencies Middle"
labels[call("find_usages", {"classId": "TestLib.Base"})] = "find_usages Base"
labels[call("analyze_impact", {"classIds": ["TestLib.Base"]})] = "analyze_impact Base"
labels[call("get_class_resources", {"classId": "TestLib.WithRes"})] = "get_class_resources WithRes"
labels[call("get_resource_warnings", {})] = "get_resource_warnings"
proc.stdin.close()
try:
    proc.wait(timeout=10)
except Exception:
    proc.kill()

def payload(msg):
    r = msg.get("result", {})
    c = r.get("content")
    if c and isinstance(c, list) and "text" in c[0]:
        try:
            return json.loads(c[0]["text"])
        except Exception:
            return c[0]["text"]
    return r or msg.get("error")

for i in sorted(responses):
    if i == 1:
        continue
    print(f"--- {labels.get(i, i)} ---")
    print(json.dumps(payload(responses[i]), indent=1)[:1400])
