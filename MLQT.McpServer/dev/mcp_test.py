#!/usr/bin/env python3
"""Dev driver: start the MLQT MCP server over stdio, send a scripted sequence of tool calls
(with a pause after the load so it completes), and print each response's decoded payload.

Usage: python mcp_test.py <exe> <load_path> [--phase quality|session]
"""
import json, subprocess, sys, threading, time

exe = sys.argv[1]
load_path = sys.argv[2]

reqs_phase1 = [
    ("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "t", "version": "1"}}),
    ("notifications/initialized", None),
    ("tools/call", {"name": "load_library", "arguments": {"path": load_path}}),
]

MESSY = 'model M "d"\n  Real   y=2   "yy";\nequation\n y=1;\nend M;'

reqs_phase2 = [
    ("tools/call", {"name": "get_style_settings", "arguments": {}}),
    ("tools/call", {"name": "format_code", "arguments": {"source": MESSY}}),
    ("tools/call", {"name": "check_style", "arguments": {"source": 'model B\n Real p;\nequation\n p=1;\nend B;',
                                                           "settings": {"ClassHasDescription": True, "ParameterHasDescription": True}}}),
    ("tools/call", {"name": "check_style", "arguments": {"source": 'model P "aa"\n  Real q "The postion of q";\nequation\n q=1;\nend P;',
                                                           "settings": {"SpellCheckDescription": True}}}),
    ("tools/call", {"name": "spell_check", "arguments": {"classId": "TestModel"}}),
    ("tools/call", {"name": "spelling_suggestions", "arguments": {"word": "postion"}}),
    ("tools/call", {"name": "check_class", "arguments": {"classId": "TestModel",
                                                          "settings": {"ClassHasDocumentationInfo": True, "SpellCheckDescription": True}}}),
    ("tools/call", {"name": "list_issues", "arguments": {}}),
    ("tools/call", {"name": "format_class", "arguments": {"classId": "TestModel", "preview": True}}),
    ("tools/call", {"name": "correct_spelling", "arguments": {"classId": "TestModel", "oldWord": "postion", "newWord": "position"}}),
]

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

t = threading.Thread(target=reader, daemon=True)
t.start()

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
        # Wait for this response before sending the next (sequential, like a real client).
        deadline = time.time() + 30
        while mid not in responses and time.time() < deadline:
            time.sleep(0.05)

for m, p in reqs_phase1:
    send(m, p)
for m, p in reqs_phase2:
    send(m, p)
proc.stdin.close()
try:
    proc.wait(timeout=10)
except Exception:
    proc.kill()

def payload(msg):
    r = msg.get("result", {})
    content = r.get("content")
    if content and isinstance(content, list) and content and "text" in content[0]:
        try:
            return json.loads(content[0]["text"])
        except Exception:
            return content[0]["text"]
    return r or msg.get("error")

for i in sorted(responses):
    print(f"--- id {i} ---")
    print(json.dumps(payload(responses[i]), indent=1)[:1600])
