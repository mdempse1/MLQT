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

# Case matters for the suffixes: "items" ends in "ms" too, and must be compared, not masked.
TIME_KEY = re.compile(r"(?i:elapsed|duration)|Ms$|Seconds$")
GUID = re.compile(r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")


PICTURED = """package Pictured "Things with diagrams to draw"
  connector Pin "A pin"
    Real v;
    flow Real i;
    annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}}, fillColor={0,0,255},
      fillPattern=FillPattern.Solid)}), Diagram(graphics={Rectangle(extent={{-40,-40},{40,40}},
      fillColor={0,0,255}, fillPattern=FillPattern.Solid)}));
  end Pin;

  model Part "Two pins"
    Pin p annotation (Placement(transformation(extent={{-110,-10},{-90,10}})));
    Pin n annotation (Placement(transformation(extent={{90,-10},{110,10}})));
  equation
    p.i + n.i = 0;
    annotation (Icon(graphics={Rectangle(extent={{-90,-40},{90,40}}, lineColor={0,0,0}),
      Text(extent={{-80,30},{80,-30}}, textString="%name")}));
  end Part;

  model Wired "Two parts in a row"
    Part a annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
    Part b annotation (Placement(transformation(extent={{40,-10},{60,10}})));
    Part c annotation (Placement(transformation(extent={{-10,-60},{10,-40}})));
  equation
    connect(a.n, b.p) annotation (Line(points={{-40,0},{40,0}}, color={0,0,255}));
    annotation (Diagram(graphics={Text(extent={{-40,40},{40,20}}, textString="wired")}));
  end Wired;
end Pictured;
"""

# Tools the session calls outside the lists in session(), by name.
CALLED = {"mlqt_get_changed_classes", "mlqt_analyze_change_impact", "mlqt_unload_library"}

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
    # A git working copy, so the changed-classes tool goes through LibGit2Sharp's native library.
    # Fixed dates make the commit's hash the same every run, and no line-ending conversion keeps the
    # one edit below an edit to one class.
    git_env = {**os.environ, "GIT_AUTHOR_DATE": "2026-01-01T00:00:00Z", "GIT_COMMITTER_DATE": "2026-01-01T00:00:00Z"}
    for command in (["init", "-q"], ["config", "core.autocrlf", "false"], ["add", "-A"],
                    ["-c", "user.name=check", "-c", "user.email=check@example.invalid", "commit", "-qm", "fixture"]):
        subprocess.run(["git", "-C", root, *command], check=True, env=git_env)
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
            ("diagram_image", "mlqt_get_diagram_image", {"classId": "Pictured.Wired", "width": 400}),
        ]
        # Every other tool once, so each one's own code has run under protection: the edits as
        # previews, which run the edit in full and write nothing, then a few real writes and a reload.
        calls += [
            ("class_source", "mlqt_get_class_source", {"classId": "Smoke.Described", "includeAnnotations": True}),
            ("search_classes", "mlqt_search_classes", {"query": "Described"}),
            ("package_tree", "mlqt_get_package_tree", {"rootClassId": "Smoke", "maxDepth": 2}),
            ("dependencies", "mlqt_get_dependencies", {"classId": "Smoke.UsesTheAlias"}),
            ("impact", "mlqt_analyze_impact", {"classIds": ["Smoke.Fraction"]}),
            ("diagram_layout", "mlqt_get_diagram_layout", {"classId": "Pictured.Wired"}),
            ("list_connections", "mlqt_list_connections", {"classId": "Pictured.Wired"}),
            ("class_elements", "mlqt_list_class_elements", {"classId": "Smoke.Undescribed", "includeProtected": True}),
            ("class_documentation", "mlqt_get_class_documentation", {"classId": "Smoke.Described"}),
            ("search_text", "mlqt_search_text", {"query": "imperfect"}),
            ("search_by_interface", "mlqt_search_by_interface", {"classType": "model", "minParameters": 1}),
            ("class_resources", "mlqt_get_class_resources", {"classId": "Smoke.Described"}),
            ("resource_usages", "mlqt_find_resource_usages", {"resolvedFilePath": os.path.join(root, "none.txt")}),
            ("resource_warnings", "mlqt_get_resource_warnings", {}),
            ("discover_libraries", "mlqt_discover_libraries", {"repositoryId": "<from load_repository>"}),
            ("format_code", "mlqt_format_code", {"source": 'model M "d"\n  Real   y=2   "yy";\nequation\n y=1;\nend M;'}),
            ("format_class", "mlqt_format_class", {"classId": "Smoke.Undescribed", "declarationOrder": True, "preview": True}),
            ("check_style", "mlqt_check_style",
             {"source": MISSPELLED, "settings": {"spellCheckDescription": True, "classHasDescription": True}}),
            ("check_class", "mlqt_check_class", {"classId": "Smoke.Undescribed", "settings": {"parameterHasDescription": True}}),
            ("set_placement", "mlqt_set_component_placement",
             {"classId": "Pictured.Wired", "componentName": "c", "x1": -10, "y1": -80, "x2": 10, "y2": -60, "preview": True}),
            ("set_class_description", "mlqt_set_class_description",
             {"classId": "Smoke.Undescribed", "description": "Now described", "preview": True}),
            ("set_component_description", "mlqt_set_component_description",
             {"classId": "Smoke.Undescribed", "componentName": "gain", "description": "The gain", "preview": True}),
            ("set_class_documentation", "mlqt_set_class_documentation",
             {"classId": "Smoke.Described", "info": "<html><p>Info</p></html>", "preview": True}),
            ("update_class_source", "mlqt_update_class_source",
             {"classId": "Smoke.Undescribed", "preview": True,
              "newSource": 'model Undescribed "x"\n  parameter Real gain = 2;\n  Real state;\nend Undescribed;'}),
            ("create_class", "mlqt_create_class", {"parentId": "Smoke", "source": 'model Added "A new one"\nend Added;', "preview": True}),
            ("delete_class", "mlqt_delete_class", {"classId": "Smoke.UsesTheAlias", "preview": True}),
            ("move_class", "mlqt_move_class", {"classId": "Smoke.Fraction", "newParentId": "Pictured", "preview": True}),
            ("rename_class", "mlqt_rename_class", {"classId": "Smoke.Fraction", "newName": "Ratio", "preview": True}),
            ("correct_spelling", "mlqt_correct_spelling",
             {"classId": "Smoke.Described", "oldWord": "thing", "newWord": "item", "preview": True}),
            ("add_component", "mlqt_add_component", {"classId": "Smoke.Undescribed", "type": "Real", "name": "extra", "preview": True}),
            ("remove_component", "mlqt_remove_component", {"classId": "Smoke.Undescribed", "name": "state", "preview": True}),
            ("set_modifier", "mlqt_set_component_modifier", {"classId": "Smoke.Undescribed", "name": "gain", "modifier": "= 3", "preview": True}),
            ("add_extends", "mlqt_add_extends", {"classId": "Smoke.UsesTheAlias", "baseType": "Smoke.Undescribed", "preview": True}),
            ("add_import", "mlqt_add_import", {"classId": "Smoke.Described", "import": "Modelica.Units.SI", "preview": True}),
            ("add_equation", "mlqt_add_equation", {"classId": "Smoke.Undescribed", "equation": "state = gain", "preview": True}),
            ("add_statement", "mlqt_add_statement", {"classId": "Smoke.Undescribed", "statement": "state := gain;", "preview": True}),
            ("add_connection", "mlqt_add_connection", {"classId": "Pictured.Wired", "portA": "c.p", "portB": "a.p", "preview": True}),
            ("remove_connection", "mlqt_remove_connection", {"classId": "Pictured.Wired", "portA": "a.n", "portB": "b.p", "preview": True}),
            ("batch_edit", "mlqt_batch_edit", {"preview": True, "operations": [
                {"op": "add_component", "classId": "Smoke.Undescribed", "type": "Real", "name": "y"},
                {"op": "add_equation", "classId": "Smoke.Undescribed", "equation": "y = state"}]}),
            ("edit_class", "mlqt_edit_class", {"classId": "Smoke.Undescribed", "preview": True, "operations": [
                {"op": "replace", "old": "parameter Real gain = 1;", "new": "parameter Real gain = 2;"},
                {"op": "add_component", "type": "Real", "name": "y"}]}),
            ("suppress_rule", "mlqt_suppress_rule",
             {"classId": "Smoke.Undescribed", "ruleId": "MLQT.Doc.ClassDescription", "reason": "check", "preview": True}),
            ("accept_spelling", "mlqt_accept_spelling_in_class", {"classId": "Smoke.Described", "word": "Thingy", "preview": True}),
            # Real writes, through the file persistence and encoding code, then a reload from disk.
            ("write_description", "mlqt_set_class_description", {"classId": "Smoke.Undescribed", "description": "Now described"}),
            ("write_suppression", "mlqt_suppress_rule",
             {"classId": "Smoke.Undescribed", "ruleId": "MLQT.Doc.ParameterDescription", "reason": "check"}),
            ("reload", "mlqt_reload", {}),
            ("class_source_after_writes", "mlqt_get_class_source", {"classId": "Smoke.Undescribed"}),
        ]
        for label, tool, arguments in calls:
            if "<from load_repository>" in arguments.values():
                loaded = (transcript["load_repository"].get("content") or [{}])[0]
                found = loaded.get("repositoryId") if isinstance(loaded, dict) else None
                arguments = {k: (found or "") if v == "<from load_repository>" else v for k, v in arguments.items()}
            started = time.monotonic()
            answer = server.request("tools/call", {"name": tool, "arguments": arguments})
            timings[label] = round((time.monotonic() - started) * 1000)
            transcript[label] = content_of(answer)

        # An edit to one class, then the question only a VCS can answer.
        package = os.path.join(root, "Libraries", "Smoke", "package.mo")
        with open(package, encoding="utf-8", newline="") as f:
            source = f.read()
        with open(package, "w", encoding="utf-8", newline="") as f:
            f.write(source.replace('"Has everything the rules ask for"', '"Has everything the rules ask for, and more"'))
        # Read leniently: a build that renamed the field should show up as a difference, not a crash.
        loaded = (transcript["load_repository"].get("content") or [{}])[0]
        repository_id = loaded.get("repositoryId") if isinstance(loaded, dict) else None
        transcript["changed_classes"] = content_of(server.request(
            "tools/call", {"name": "mlqt_get_changed_classes", "arguments": {"repositoryId": repository_id}})) \
            if repository_id else {"skipped": "load_repository returned no repositoryId"}
        # The reload above reset the opt-in analysis, which the impact question needs.
        transcript["reanalyze"] = content_of(server.request(
            "tools/call", {"name": "mlqt_analyze_dependencies", "arguments": {}}))
        transcript["change_impact"] = content_of(server.request(
            "tools/call", {"name": "mlqt_analyze_change_impact", "arguments": {"repositoryId": repository_id}})) \
            if repository_id else {"skipped": "load_repository returned no repositoryId"}
        transcript["unload"] = content_of(server.request(
            "tools/call", {"name": "mlqt_unload_library", "arguments": {"library": "Smoke"}}))
        # Every tool was called, or the session no longer proves what it claims to.
        transcript["toolsNotCalled"] = sorted(set(transcript["tools"]) - CALLED - {t for _, t, _ in calls})

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
                "check": normalise(sorted_findings(check), library)})


def sorted_findings(result):
    """A check's findings in a fixed order: the server lists them as its workers finish."""
    for block in result.get("content", []):
        if isinstance(block, dict) and isinstance(block.get("findings"), list):
            block["findings"].sort(key=lambda f: json.dumps(f, sort_keys=True))
    return result


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
