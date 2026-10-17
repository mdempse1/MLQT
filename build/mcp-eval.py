#!/usr/bin/env python3
"""Measure how well an agent uses the MLQT MCP server, task by task.

Each task in TestFixtures/McpEval/tasks.json is run headless through Claude Code against a fresh copy
of the fixture library, with the MLQT server as the only MCP server and its tool-usage log pointed at
that run. Afterwards the files and the final answer are checked, and the transcript is read for what
the agent did: which tools it called, which of them failed, how many tokens and seconds it took, and
whether it went round the server to read or edit .mo files with its own tools.

    python build/mcp-eval.py run                       # every task once
    python build/mcp-eval.py run --repeats 3 --label baseline
    python build/mcp-eval.py run --tasks rename-class,impact --tool-search false
    python build/mcp-eval.py report McpEvalRuns/<run>  # rebuild report.md from results.json
    python build/mcp-eval.py compare McpEvalRuns/<a> McpEvalRuns/<b>
    python build/mcp-eval.py check-tasks               # no model calls: every task fails before it is done

The Modelica Standard Library is not in the repository, so say where it is with --msl or
MLQT_EVAL_MSL (the directory holding MSL's package.mo, e.g. ...\\ModelicaStandardLibrary\\Modelica).

Every run calls the model and costs money: twelve tasks once is a few dollars at most, but --repeats
multiplies it. The work directories live in the system temp folder, outside this repository, so the
agent does not pick up MLQT's own CLAUDE.md; each is a git repository of its own so the VCS tools work.
"""

import argparse
import concurrent.futures
import datetime
import json
import os
import re
import shutil
import statistics
import subprocess
import sys
import tempfile
import threading
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
FIXTURE = REPO / "TestFixtures" / "McpEval"
SERVER_NAME = "mlqt"
MCP_PREFIX = f"mcp__{SERVER_NAME}__"
# Claude Code's own tools that can read, search or change files without the server.
FILE_TOOLS = {"Read", "Edit", "Write", "MultiEdit", "Grep", "Glob", "NotebookEdit", "Bash", "PowerShell"}
DEFAULT_ALLOWED = ["mcp__mlqt", "Read", "Grep", "Glob", "Edit", "Write", "ToolSearch"]

_print_lock = threading.Lock()


def log(message):
    with _print_lock:
        print(message, flush=True)


# --------------------------------------------------------------------------------------------- tools

def short_name(tool):
    """mcp__mlqt__mlqt_rename_class -> rename_class; built-in tools are left as they are."""
    if tool.startswith(MCP_PREFIX):
        tool = tool[len(MCP_PREFIX):]
        if tool.startswith("mlqt_"):
            tool = tool[len("mlqt_"):]
    return tool


def is_mlqt(tool):
    return tool.startswith(MCP_PREFIX)


def is_bypass(tool):
    """A built-in file tool. The work directory holds nothing but the library and the MSL is Modelica too,
    so any such call read, searched or changed Modelica source without going through MLQT."""
    return tool in FILE_TOOLS


def find_server(explicit):
    if explicit:
        path = Path(explicit)
        if not path.exists():
            sys.exit(f"server not found: {path}")
        return path.resolve()
    exe = "MLQT.McpServer.exe" if os.name == "nt" else "MLQT.McpServer"
    candidates = [p for p in (REPO / "MLQT.McpServer" / "bin").glob(f"*/net10.0/{exe}")]
    if not candidates:
        sys.exit("no built MLQT.McpServer found under MLQT.McpServer/bin - build it, or pass --server")
    return max(candidates, key=lambda p: p.stat().st_mtime).resolve()


def find_claude():
    claude = shutil.which("claude")
    if not claude:
        sys.exit("the claude CLI is not on PATH")
    return claude


# --------------------------------------------------------------------------------------------- setup

def prepare_workdir(keep_root):
    work = Path(tempfile.mkdtemp(prefix="mlqt-eval-", dir=keep_root))
    shutil.copytree(FIXTURE / "EvalLib", work / "EvalLib")
    git = ["git", "-c", "user.name=mcp-eval", "-c", "user.email=mcp-eval@localhost", "-c", "core.autocrlf=false"]
    for args in (["init", "-q"], ["add", "-A"], ["commit", "-q", "-m", "fixture"]):
        subprocess.run(git + args, cwd=work, check=True, capture_output=True)
    return work


def render_prompt(preamble, task, work, msl):
    lib = str(work / "EvalLib")
    return (preamble + "\n\n" + task["prompt"]).replace("{lib}", lib).replace("{msl}", str(msl))


def kill_tree(proc):
    if os.name == "nt":
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)], capture_output=True)
    else:
        proc.kill()


# --------------------------------------------------------------------------------------------- one run

def run_one(task, repeat, ctx):
    run_id = f"{task['id']}#{repeat}"
    out_dir = ctx["run_dir"] / "runs" / f"{task['id']}-{repeat}"
    out_dir.mkdir(parents=True, exist_ok=True)
    work = prepare_workdir(ctx["work_root"])
    server_log = out_dir / "server-log.jsonl"

    mcp_config = out_dir / "mcp-config.json"
    mcp_config.write_text(json.dumps({"mcpServers": {SERVER_NAME: {
        "command": str(ctx["server"]),
        "args": [],
        "env": {"MLQT_MCP_TOOL_LOG": str(server_log.resolve())},
    }}}, indent=2), encoding="utf-8")

    cmd = [ctx["claude"], "-p",
           "--output-format", "stream-json", "--verbose",
           "--mcp-config", str(mcp_config.resolve()), "--strict-mcp-config",
           "--permission-mode", "dontAsk",
           "--no-session-persistence",
           "--setting-sources", ctx["setting_sources"],
           "--allowedTools", *ctx["allowed_tools"]]
    if ctx["model"]:
        cmd += ["--model", ctx["model"]]

    env = dict(os.environ)
    env.pop("CLAUDECODE", None)  # a nested claude refuses to start inside a Claude Code session
    if ctx["tool_search"] is not None:
        env["ENABLE_TOOL_SEARCH"] = ctx["tool_search"]

    prompt = render_prompt(ctx["preamble"], task, work, ctx["msl"])
    (out_dir / "prompt.txt").write_text(prompt, encoding="utf-8")

    log(f"  start  {run_id}")
    started = time.time()
    timed_out = False
    proc = subprocess.Popen(cmd, cwd=work, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, encoding="utf-8", errors="replace")
    try:
        stdout, stderr = proc.communicate(prompt, timeout=ctx["timeout"])
    except subprocess.TimeoutExpired:
        kill_tree(proc)
        stdout, stderr = proc.communicate()
        timed_out = True
    wall = time.time() - started

    (out_dir / "transcript.jsonl").write_text(stdout or "", encoding="utf-8")
    if stderr:
        (out_dir / "stderr.txt").write_text(stderr, encoding="utf-8")

    result = analyse_transcript(stdout or "", task)
    result.update(run=run_id, task=task["id"], group=task.get("group", ""), repeat=repeat,
                  wall_s=round(wall, 1), timed_out=timed_out, exit_code=proc.returncode)
    result["checks"] = [run_check(c, work, result.get("answer", "")) for c in task.get("checks", [])]
    result["passed"] = (not timed_out and not result.get("infra_error")
                        and all(c["passed"] for c in result["checks"]))
    result["server"] = read_server_log(server_log)

    diff = subprocess.run(["git", "-c", "core.autocrlf=false", "diff", "--no-color"], cwd=work,
                          capture_output=True, encoding="utf-8", errors="replace")
    untracked = subprocess.run(["git", "status", "--porcelain", "--untracked-files=all"], cwd=work,
                               capture_output=True, encoding="utf-8", errors="replace")
    (out_dir / "changes.diff").write_text(diff.stdout + "\n# git status\n" + untracked.stdout, encoding="utf-8")
    (out_dir / "result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")

    if ctx["keep_work"]:
        result["work_dir"] = str(work)
    else:
        shutil.rmtree(work, ignore_errors=True)

    status = "PASS" if result["passed"] else ("TIMEOUT" if timed_out else "FAIL")
    log(f"  {status:<6} {run_id}  {result['tool_calls']} calls, {result['tool_errors']} errors, "
        f"{result['tokens_in'] + result['tokens_out']:,} tokens, {wall:.0f}s")
    return result


def analyse_transcript(text, task):
    calls = []          # one per tool_use, in order
    by_id = {}
    answer_parts = []
    final = {}
    init = {}

    for line in text.splitlines():
        line = line.strip()
        if not line.startswith("{"):
            continue
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        kind = event.get("type")
        if kind == "system" and event.get("subtype") == "init":
            init = event
        elif kind == "assistant":
            for block in event.get("message", {}).get("content", []) or []:
                if block.get("type") == "tool_use":
                    call = {"tool": block.get("name", ""), "input": block.get("input"), "error": False,
                            "result_chars": 0}
                    calls.append(call)
                    by_id[block.get("id")] = call
                elif block.get("type") == "text":
                    answer_parts.append(block.get("text", ""))
        elif kind == "user":
            content = event.get("message", {}).get("content", [])
            if not isinstance(content, list):
                continue
            for block in content:
                if block.get("type") != "tool_result":
                    continue
                call = by_id.get(block.get("tool_use_id"))
                if call is None:
                    continue
                call["error"] = bool(block.get("is_error"))
                body = block.get("content")
                call["result_chars"] = len(body if isinstance(body, str) else json.dumps(body or ""))
        elif kind == "result":
            final = event

    usage = final.get("usage") or {}
    servers = {s.get("name"): s.get("status") for s in init.get("mcp_servers", []) or []}
    available = sorted(short_name(t) for t in init.get("tools", []) or [] if is_mlqt(t))

    infra_error = None
    if not init:
        infra_error = "no init event - claude did not start"
    elif servers.get(SERVER_NAME) not in ("connected", None):
        infra_error = f"MLQT server status: {servers.get(SERVER_NAME)}"
    elif not final:
        infra_error = "no result event - the run did not finish"

    expected = task.get("expected_tools", [])
    used = {short_name(c["tool"]) for c in calls}
    expected_hits = [any(alt in used for alt in group.split("|")) for group in expected]

    return {
        "answer": final.get("result") or "\n".join(answer_parts),
        "infra_error": infra_error,
        "result_is_error": bool(final.get("is_error")),
        "duration_s": round((final.get("duration_ms") or 0) / 1000, 1),
        "turns": final.get("num_turns") or 0,
        "cost_usd": final.get("total_cost_usd") or 0,
        "tokens_in": (usage.get("input_tokens") or 0) + (usage.get("cache_read_input_tokens") or 0)
                     + (usage.get("cache_creation_input_tokens") or 0),
        "tokens_out": usage.get("output_tokens") or 0,
        "model": init.get("model", ""),
        "mlqt_tools_available": available,
        "tool_calls": len(calls),
        "mlqt_calls": sum(1 for c in calls if is_mlqt(c["tool"])),
        "tool_search_calls": sum(1 for c in calls if c["tool"] == "ToolSearch"),
        "tool_errors": sum(1 for c in calls if c["error"]),
        "bypass_calls": sum(1 for c in calls if is_bypass(c["tool"])),
        "expected_hit": sum(expected_hits),
        "expected_total": len(expected),
        "calls": [{"tool": short_name(c["tool"]), "mlqt": is_mlqt(c["tool"]), "error": c["error"],
                   "result_chars": c["result_chars"],
                   "bypass": is_bypass(c["tool"])} for c in calls],
    }


def read_server_log(path):
    entries = []
    if path.exists():
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            try:
                entry = json.loads(line)
            except json.JSONDecodeError:
                continue
            entries.append({"tool": short_name(MCP_PREFIX + entry.get("tool", "")),
                            "ms": entry.get("ms", 0), "error": bool(entry.get("error"))})
    return entries


# --------------------------------------------------------------------------------------------- checks

def run_check(check, work, answer):
    kind = check["type"]
    pattern = check.get("pattern", "")
    detail = ""
    try:
        if kind in ("answer_matches", "answer_not_matches"):
            found = re.search(pattern, answer or "") is not None
            passed = found if kind == "answer_matches" else not found
        elif kind in ("file_matches", "file_not_matches"):
            path = work / check["path"]
            if not path.exists():
                passed, detail = kind == "file_not_matches", f"{check['path']} does not exist"
            else:
                found = re.search(pattern, path.read_text(encoding="utf-8", errors="replace")) is not None
                passed = found if kind == "file_matches" else not found
        elif kind in ("glob_matches", "glob_not_matches"):
            hits = [p for p in work.glob(check["glob"])
                    if re.search(pattern, p.read_text(encoding="utf-8", errors="replace"))]
            passed = bool(hits) if kind == "glob_matches" else not hits
            detail = ", ".join(str(p.relative_to(work)) for p in hits)
        elif kind == "file_exists":
            passed = (work / check["path"]).exists()
        elif kind == "command":
            proc = subprocess.run(check["command"], cwd=work, shell=True, capture_output=True, text=True,
                                  timeout=check.get("timeout", 300))
            passed, detail = proc.returncode == 0, (proc.stdout + proc.stderr)[-500:]
        else:
            passed, detail = False, f"unknown check type {kind}"
    except Exception as exc:  # a broken check is a failed check, not a crashed run
        passed, detail = False, f"{type(exc).__name__}: {exc}"
    return {"type": kind, "target": check.get("path") or check.get("glob") or "answer",
            "pattern": pattern, "passed": passed, "detail": detail}


# --------------------------------------------------------------------------------------------- report

def mean(values):
    values = list(values)
    return statistics.fmean(values) if values else 0


def summarise(results):
    tasks = {}
    for r in results:
        tasks.setdefault(r["task"], []).append(r)
    rows = []
    for task_id, runs in tasks.items():
        rows.append({
            "task": task_id,
            "group": runs[0].get("group", ""),
            "runs": len(runs),
            "pass_rate": mean(1 if r["passed"] else 0 for r in runs),
            "tool_calls": mean(r["tool_calls"] for r in runs),
            "mlqt_calls": mean(r["mlqt_calls"] for r in runs),
            "tool_errors": mean(r["tool_errors"] for r in runs),
            "bypass_calls": mean(r["bypass_calls"] for r in runs),
            "tool_search_calls": mean(r["tool_search_calls"] for r in runs),
            "expected_hit": mean(r["expected_hit"] / r["expected_total"] if r["expected_total"] else 1 for r in runs),
            "tokens_in": mean(r["tokens_in"] for r in runs),
            "tokens_out": mean(r["tokens_out"] for r in runs),
            "duration_s": mean(r["duration_s"] or r["wall_s"] for r in runs),
            "cost_usd": mean(r["cost_usd"] for r in runs),
        })
    return rows


def tool_table(results):
    tools = {}
    for r in results:
        seen = set()
        for c in r["calls"]:
            t = tools.setdefault(c["tool"], {"calls": 0, "runs": 0, "errors": 0, "chars": [], "mlqt": c["mlqt"],
                                             "bypass": 0, "ms": []})
            t["calls"] += 1
            t["errors"] += c["error"]
            t["bypass"] += c["bypass"]
            t["chars"].append(c["result_chars"])
            if c["tool"] not in seen:
                t["runs"] += 1
                seen.add(c["tool"])
        for s in r.get("server", []):
            if s["tool"] in tools:
                tools[s["tool"]]["ms"].append(s["ms"])
    return tools


def write_report(run_dir, results, config):
    rows = summarise(results)
    tools = tool_table(results)
    available = sorted({t for r in results for t in r.get("mlqt_tools_available", [])})
    n = len(results)

    lines = [f"# MCP tool evaluation - {config.get('label') or run_dir.name}", ""]
    lines += ["| Setting | Value |", "|---|---|"]
    for key in ("started", "model", "server", "server_built", "tool_search", "repeats", "allowed_tools"):
        value = config.get(key)
        if key == "model" and not value:
            value = ", ".join(sorted({r.get("model", "") for r in results})) or "(default)"
        lines.append(f"| {key} | {', '.join(value) if isinstance(value, list) else value} |")
    lines.append(f"| MLQT tools offered | {len(available)} |")
    lines.append("")

    passed = sum(r["passed"] for r in results)
    lines += ["## Overall", "",
              f"- **{passed} of {n} runs passed** ({passed / n:.0%})" if n else "- no runs",
              f"- tool calls per run: {mean(r['tool_calls'] for r in results):.1f} "
              f"(MLQT {mean(r['mlqt_calls'] for r in results):.1f}, ToolSearch {mean(r['tool_search_calls'] for r in results):.1f})",
              f"- tool errors per run: {mean(r['tool_errors'] for r in results):.2f}",
              f"- built-in file tool calls (Modelica read, searched or changed without MLQT): {sum(r['bypass_calls'] for r in results)}",
              f"- expected tools used: {mean(row['expected_hit'] for row in rows):.0%}",
              f"- tokens per run: {mean(r['tokens_in'] for r in results):,.0f} in, {mean(r['tokens_out'] for r in results):,.0f} out",
              f"- total cost: ${sum(r['cost_usd'] for r in results):.2f}", ""]

    lines += ["## By task", "",
              "| Task | Group | Runs | Pass | Calls | MLQT | Errors | Bypass | Expected tools | Tokens in | Tokens out | Seconds | Cost |",
              "|---|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|"]
    for row in rows:
        lines.append(f"| {row['task']} | {row['group']} | {row['runs']} | {row['pass_rate']:.0%} | {row['tool_calls']:.1f} | "
                     f"{row['mlqt_calls']:.1f} | {row['tool_errors']:.1f} | {row['bypass_calls']:.1f} | {row['expected_hit']:.0%} | "
                     f"{row['tokens_in']:,.0f} | {row['tokens_out']:,.0f} | {row['duration_s']:.0f} | ${row['cost_usd']:.3f} |")
    lines.append("")

    lines += ["## By tool", "",
              "| Tool | Calls | Runs using it | Errors | Mean result chars | Mean server ms | Bypass |",
              "|---|--:|--:|--:|--:|--:|--:|"]
    for name, t in sorted(tools.items(), key=lambda kv: (-kv[1]["calls"], kv[0])):
        label = name if t["mlqt"] else f"*{name}* (built-in)"
        ms = f"{mean(t['ms']):.0f}" if t["ms"] else ""
        lines.append(f"| {label} | {t['calls']} | {t['runs']} | {t['errors']} | {mean(t['chars']):,.0f} | {ms} | {t['bypass'] or ''} |")
    lines.append("")

    unused = [t for t in available if t not in tools]
    lines += [f"## MLQT tools never called ({len(unused)} of {len(available)})", "",
              ", ".join(f"`{t}`" for t in unused) or "(none)", ""]

    failures = [r for r in results if not r["passed"]]
    if failures:
        lines += ["## Failures", ""]
        for r in failures:
            reason = r.get("infra_error") or ("timed out" if r["timed_out"] else "")
            failed = [f"{c['type']} `{c['target']}` /{c['pattern']}/ {c['detail']}".strip()
                      for c in r["checks"] if not c["passed"]]
            lines.append(f"- **{r['run']}** {reason}")
            lines += [f"  - {f}" for f in failed]
            lines.append(f"  - transcript: `runs/{r['task']}-{r['repeat']}/transcript.jsonl`")
        lines.append("")

    (run_dir / "report.md").write_text("\n".join(lines), encoding="utf-8")
    return run_dir / "report.md"


# --------------------------------------------------------------------------------------------- commands

def cmd_run(args):
    spec = json.loads((FIXTURE / "tasks.json").read_text(encoding="utf-8"))
    tasks = spec["tasks"]
    if args.tasks:
        wanted = set(args.tasks.split(","))
        unknown = wanted - {t["id"] for t in tasks}
        if unknown:
            sys.exit(f"unknown task(s): {', '.join(sorted(unknown))}")
        tasks = [t for t in tasks if t["id"] in wanted]

    msl = args.msl or os.environ.get("MLQT_EVAL_MSL")
    if not msl or not (Path(msl) / "package.mo").exists():
        sys.exit("pass --msl (or set MLQT_EVAL_MSL) to the Modelica Standard Library directory holding package.mo")

    server = find_server(args.server)
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    run_dir = Path(args.output or REPO / "McpEvalRuns") / (stamp + (f"-{args.label}" if args.label else ""))
    run_dir.mkdir(parents=True, exist_ok=True)

    config = {
        "label": args.label, "started": stamp, "model": args.model, "server": str(server),
        "server_built": datetime.datetime.fromtimestamp(server.stat().st_mtime).strftime("%Y-%m-%d %H:%M"),
        "tool_search": args.tool_search or "(Claude Code default)", "repeats": args.repeats,
        "allowed_tools": args.allowed_tools, "tasks": [t["id"] for t in tasks],
    }
    (run_dir / "config.json").write_text(json.dumps(config, indent=2), encoding="utf-8")

    ctx = {"run_dir": run_dir, "server": server, "claude": find_claude(), "msl": Path(msl).resolve(),
           "preamble": spec.get("preamble", ""), "model": args.model, "tool_search": args.tool_search,
           "timeout": args.timeout, "allowed_tools": args.allowed_tools, "keep_work": args.keep_work,
           "setting_sources": args.setting_sources, "work_root": None}

    jobs = [(t, r) for r in range(1, args.repeats + 1) for t in tasks]
    log(f"{len(jobs)} runs into {run_dir}")
    results = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.parallel) as pool:
        for future in concurrent.futures.as_completed([pool.submit(run_one, t, r, ctx) for t, r in jobs]):
            results.append(future.result())
    results.sort(key=lambda r: ([t["id"] for t in tasks].index(r["task"]), r["repeat"]))

    (run_dir / "results.json").write_text(json.dumps({"config": config, "results": results}, indent=2), encoding="utf-8")
    report = write_report(run_dir, results, config)
    passed = sum(r["passed"] for r in results)
    log(f"{passed}/{len(results)} passed - {report}")
    return 0 if passed == len(results) else 1


def cmd_check_tasks(args):
    """Every task must fail on the untouched fixture with an empty answer - otherwise one of its checks
    passes without the agent doing anything, and its pass rate means nothing. Free: no model is called."""
    spec = json.loads((FIXTURE / "tasks.json").read_text(encoding="utf-8"))
    hollow = 0
    for task in spec["tasks"]:
        checks = [run_check(c, FIXTURE, "") for c in task.get("checks", [])]
        if not checks or all(c["passed"] for c in checks):
            hollow += 1
            print(f"HOLLOW {task['id']}: passes before anything is done")
        else:
            print(f"ok     {task['id']}: {sum(not c['passed'] for c in checks)} of {len(checks)} checks fail on the fixture")
    return 1 if hollow else 0


def load_results(run_dir):
    data = json.loads((Path(run_dir) / "results.json").read_text(encoding="utf-8"))
    return data["config"], data["results"]


def cmd_report(args):
    config, results = load_results(args.run_dir)
    print(write_report(Path(args.run_dir), results, config))
    return 0


def cmd_compare(args):
    (config_a, a), (config_b, b) = load_results(args.a), load_results(args.b)
    rows_a = {r["task"]: r for r in summarise(a)}
    rows_b = {r["task"]: r for r in summarise(b)}
    name_a = config_a.get("label") or Path(args.a).name
    name_b = config_b.get("label") or Path(args.b).name
    metrics = [("pass_rate", "{:.0%}"), ("tool_calls", "{:.1f}"), ("tool_errors", "{:.1f}"),
               ("bypass_calls", "{:.1f}"), ("tokens_in", "{:,.0f}"), ("tokens_out", "{:,.0f}"),
               ("duration_s", "{:.0f}"), ("cost_usd", "${:.3f}")]
    print(f"A = {name_a}\nB = {name_b}\n")
    print("| Task | " + " | ".join(m for m, _ in metrics) + " |")
    print("|---|" + "---|" * len(metrics))
    for task in [t for t in rows_a if t in rows_b] + ["(all)"]:
        if task == "(all)":
            ra = {m: mean(rows_a[t][m] for t in rows_a if t in rows_b) for m, _ in metrics}
            rb = {m: mean(rows_b[t][m] for t in rows_a if t in rows_b) for m, _ in metrics}
        else:
            ra, rb = rows_a[task], rows_b[task]
        cells = [f"{fmt.format(ra[m])} → {fmt.format(rb[m])}" for m, fmt in metrics]
        print(f"| {task} | " + " | ".join(cells) + " |")
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    run = sub.add_parser("run", help="run the tasks and write a report")
    run.add_argument("--tasks", help="comma-separated task ids (default: all)")
    run.add_argument("--repeats", type=int, default=1, help="runs per task (default 1; 3-5 to see variance)")
    run.add_argument("--parallel", type=int, default=1, help="runs at once (default 1)")
    run.add_argument("--model", help="model for claude --model (default: Claude Code's)")
    run.add_argument("--msl", help="Modelica Standard Library directory (default: MLQT_EVAL_MSL)")
    run.add_argument("--server", help="MLQT.McpServer executable (default: the newest build under MLQT.McpServer/bin)")
    run.add_argument("--tool-search", choices=["true", "false", "auto"],
                     help="ENABLE_TOOL_SEARCH for the agent: false loads every tool definition up front")
    run.add_argument("--allowed-tools", nargs="+", default=DEFAULT_ALLOWED,
                     help=f"tools the agent may use without asking (default: {' '.join(DEFAULT_ALLOWED)})")
    run.add_argument("--setting-sources", default="project",
                     help="claude --setting-sources; the default leaves out your user settings and hooks")
    run.add_argument("--timeout", type=int, default=600, help="seconds before a run is killed (default 600)")
    run.add_argument("--label", help="name for this run, used in the folder name and the report")
    run.add_argument("--output", help="where run folders go (default: McpEvalRuns/ at the repository root)")
    run.add_argument("--keep-work", action="store_true", help="keep each run's work directory")
    run.set_defaults(func=cmd_run)

    report = sub.add_parser("report", help="rebuild report.md from a run's results.json")
    report.add_argument("run_dir")
    report.set_defaults(func=cmd_report)

    compare = sub.add_parser("compare", help="compare two runs task by task (markdown to stdout)")
    compare.add_argument("a")
    compare.add_argument("b")
    compare.set_defaults(func=cmd_compare)

    check = sub.add_parser("check-tasks", help="prove every task fails on the untouched fixture (no model calls)")
    check.set_defaults(func=cmd_check_tasks)

    args = parser.parse_args()
    sys.exit(args.func(args))


if __name__ == "__main__":
    main()
