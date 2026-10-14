#!/usr/bin/env python3
"""Run one scripted session against an MLQT MCP server and write a normalised transcript (B502).

The transcript is what a client can see: the instructions, every tool's input schema, the result of
a few dozen calls and the .mlqt/settings.json the session wrote. Run against a plain build and an
obfuscated one, the two transcripts must be identical (Design/release-checklist.md section 7).
Timings are recorded beside the transcript, not in it, since they differ by design.

Usage:
  python mcp_session.py <server-exe> <fixture-dir> <transcript.json>
  python mcp_session.py <server-exe> --time-check <library-dir> <timing.json>
"""
import base64, hashlib, json, os, re, shutil, subprocess, sys, tempfile, threading, time

TIME_KEY = re.compile(r"(?i)(elapsed|duration|Ms$|seconds$)")
GUID = re.compile(r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")


PICTURED = """model Pictured "Has a diagram to draw"
  Real x "A value" annotation (Placement(transformation(extent={{-10,-10},{10,10}})));
equation
  x = 1;
  annotation (Diagram(graphics={Rectangle(extent={{-50,-40},{50,40}}, lineColor={0,0,255}),
    Text(extent={{-40,30},{40,10}}, textString="pictured")}));
end Pictured;
"""

MISSPELLED = """model P "A model"
  Real q "The postion of q";
equation
  q = 1;
end P;
"""


class Server:
    def __init__(self, exe):
        self.proc = subprocess.Popen([os.path.abspath(exe)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                     stderr=subprocess.DEVNULL, text=True, encoding="utf-8")
        self.responses, self.next_id = {}, 1
        self.cond = threading.Condition()
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        for line in self.proc.stdout:
            try:
                message = json.loads(line)
            except ValueError:
                continue
            if "id" in message:
                with self.cond:
                    self.responses[message["id"]] = message
                    self.cond.notify_all()

    def notify(self, method):
        self._write({"jsonrpc": "2.0", "method": method})

    def request(self, method, params, timeout=600):
        request_id, self.next_id = self.next_id, self.next_id + 1
        self._write({"jsonrpc": "2.0", "id": request_id, "method": method, "params": params})
        deadline = time.monotonic() + timeout
        with self.cond:
            while request_id not in self.responses:
                left = deadline - time.monotonic()
                if left <= 0 or self.proc.poll() is not None:
                    raise RuntimeError(f"no answer to {method} {json.dumps(params)[:200]}")
                self.cond.wait(left)
            return self.responses.pop(request_id)

    def _write(self, message):
        self.proc.stdin.write(json.dumps(message) + "\n")
        self.proc.stdin.flush()

    def close(self):
        try:
            self.proc.stdin.close()
            self.proc.wait(timeout=20)
        except Exception:
            self.proc.kill()


def initialize(server):
    answer = server.request("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                           "clientInfo": {"name": "obfuscation-check", "version": "1"}})
    server.notify("notifications/initialized")
    return answer


def content_of(answer):
    """A tool result as a client reads it: each JSON text parsed, each image reduced to its digest."""
    if "error" in answer:
        return {"protocolError": answer["error"].get("message")}
    result = answer["result"]
    blocks = []
    for block in result.get("content", []):
        if block.get("type") == "text":
            try:
                blocks.append(json.loads(block["text"]))
            except ValueError:
                blocks.append(block["text"])
        elif block.get("type") == "image":
            data = base64.b64decode(block["data"])
            blocks.append({"image": block.get("mimeType"), "sha256": hashlib.sha256(data).hexdigest()})
        else:
            blocks.append(block)
    return {"isError": result.get("isError", False), "content": blocks}


def mask_times(value):
    """Durations differ run to run by nature; what a client relies on is that the field is there."""
    if isinstance(value, dict):
        return {k: "<TIME>" if TIME_KEY.search(k) else mask_times(v) for k, v in value.items()}
    if isinstance(value, list):
        return [mask_times(v) for v in value]
    return value


def normalise(value, root):
    value = mask_times(value)
    text = json.dumps(value, sort_keys=True, ensure_ascii=False)
    for spelling in {root, root.replace("\\", "/"), root.replace("\\", "\\\\"), root.replace("/", "\\\\")}:
        text = text.replace(spelling, "<ROOT>")
    return json.loads(GUID.sub("<GUID>", text))


def session(exe, fixture, out):
    # A fixed folder name inside a fresh temporary one: the repository is named after its folder.
    scratch = tempfile.mkdtemp(prefix="mlqt-obf-")
    root = os.path.join(scratch, "Fixture")
    shutil.copytree(fixture, root)
    # Something to draw, so the diagram image goes through SkiaSharp's native rasteriser.
    pictured = os.path.join(root, "Pictured.mo")
    with open(pictured, "w", encoding="utf-8", newline="\n") as f:
        f.write(PICTURED)
    transcript, timings = {}, {}
    server = Server(exe)
    try:
        init = initialize(server)["result"]
        transcript["instructions"] = init.get("instructions")
        tools = server.request("tools/list", {})["result"]["tools"]
        transcript["tools"] = {t["name"]: {"inputSchema": t.get("inputSchema"), "description": t.get("description")}
                               for t in tools}

        calls = [
            ("server_info", "mlqt_server_info", {}),
            ("guidance", "mlqt_get_guidance", {"topic": "views"}),
            # A quoted boolean and a quoted number: the coercion filter reads the tools' schemas.
            ("create_library_preview", "mlqt_create_library",
             {"name": "Fresh", "directory": os.path.join(root, "new"), "preview": "true"}),
            ("load_repository", "mlqt_load_repository", {"path": root}),
            ("load_library", "mlqt_load_library", {"path": os.path.join(root, "Libraries", "Smoke")}),
            ("list_repositories", "mlqt_list_repositories", {}),
            ("list_libraries", "mlqt_list_libraries", {}),
            ("list_classes", "mlqt_list_classes", {"limit": "50"}),
            ("class_info", "mlqt_get_class_info", {"classId": "Smoke.Described"}),
            ("class_interface", "mlqt_get_class_interface", {"classId": "Smoke.Fraction"}),
            ("class_behavior", "mlqt_get_class_behavior", {"classId": "Smoke.Described"}),
            ("validate_references", "mlqt_validate_class_references", {"classId": "Smoke.UsesTheAlias"}),
            ("style_settings", "mlqt_get_style_settings", {}),
            # A complex argument, read through its compiled contract, and a committed file written.
            ("set_style_settings", "mlqt_set_style_settings",
             {"settings": {"classHasDescription": True, "spellCheckDescription": True}}),
            ("check_library", "mlqt_check_library", {}),
            ("findings", "mlqt_list_findings", {"limit": 200}),
            # The bundled Hunspell dictionaries are embedded resources, looked up by name.
            ("spell_check", "mlqt_spell_check", {"source": MISSPELLED}),
            ("spelling_suggestions", "mlqt_spelling_suggestions", {"word": "postion"}),
            ("analyze_dependencies", "mlqt_analyze_dependencies", {}),
            ("find_usages", "mlqt_find_usages", {"classId": "Smoke.Fraction"}),
            # SkiaSharp's native rasteriser.
            ("load_pictured", "mlqt_load_library", {"path": pictured}),
            ("diagram_image", "mlqt_get_diagram_image", {"classId": "Pictured", "width": 400}),
            ("unload", "mlqt_unload_library", {"library": "Smoke"}),
        ]
        for label, tool, arguments in calls:
            started = time.monotonic()
            answer = server.request("tools/call", {"name": tool, "arguments": arguments})
            timings[label] = round((time.monotonic() - started) * 1000)
            transcript[label] = content_of(answer)

        settings = os.path.join(root, ".mlqt", "settings.json")
        transcript["settingsFile"] = open(settings, encoding="utf-8").read() if os.path.exists(settings) else None
    finally:
        server.close()
        shutil.rmtree(scratch, ignore_errors=True)

    write(out, normalise(transcript, root))
    write(os.path.splitext(out)[0] + ".timings.json", timings)


def time_check(exe, library, out):
    server = Server(exe)
    try:
        initialize(server)
        started = time.monotonic()
        load = content_of(server.request("tools/call", {"name": "mlqt_load_library", "arguments": {"path": library}}))
        loaded = time.monotonic()
        check = content_of(server.request("tools/call", {"name": "mlqt_check_library", "arguments": {}}, timeout=3600))
        checked = time.monotonic()
    finally:
        server.close()
    write(out, {"loadMs": round((loaded - started) * 1000), "checkMs": round((checked - loaded) * 1000),
                "loadIsError": load["isError"], "checkIsError": check["isError"],
                "check": normalise(check, library)})


def write(path, value):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(value, f, indent=2, sort_keys=True, ensure_ascii=False)
        f.write("\n")


if __name__ == "__main__":
    if len(sys.argv) == 5 and sys.argv[2] == "--time-check":
        time_check(sys.argv[1], sys.argv[3], sys.argv[4])
    elif len(sys.argv) == 4:
        session(sys.argv[1], sys.argv[2], sys.argv[3])
    else:
        sys.exit(__doc__)
