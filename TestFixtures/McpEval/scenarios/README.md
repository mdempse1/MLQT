# Real-world scenarios

Two cut-down versions of real model-building sessions, run by hand: one with Dymola beside MLQT, one
with OpenModelica. The harness tasks in `../tasks.json` are small and can be checked automatically.
These are not, but they are the work the MCP server exists for, and the tool consolidation (B503,
66 tools to 16) was judged on them. Each takes 30–60 minutes,
where the sessions they come from took 1–27 hours.

| Scenario | Simulator | Cut down from |
|---|---|---|
| [openmodelica-mcpherson-corner.md](openmodelica-mcpherson-corner.md) | OpenModelica | A Kia EV4-sized car with McPherson struts at each corner (2026-10-18, 57 minutes, 200 tool calls) |
| [dymola-formula1-ers.md](dymola-formula1-ers.md) | Dymola | A 2026 Formula 1 car (2026-10-17, 86 minutes, 416 tool calls). It starts from the library that session built, and runs on the machine where Dymola is licensed |

## Running one

1. **Build and publish the MLQT server you are measuring**, and note the commit. Point the `mlqt`
   entry of the Claude configuration at it. Give that entry its own log for this run:
   `"env": { "MLQT_MCP_TOOL_LOG": "<folder>/mlqt-tool-usage-<scenario>-<label>.jsonl" }`.
2. **Register the simulator's MCP server** the scenario names, and nothing else that touches Modelica.
3. **Make an empty folder** for the run, outside any repository, and `git init` it, so the VCS tools
   have something to work with.
4. **Quit Claude completely and start it again** - the app, not only the session - so the servers start
   new, with the new settings, and open a new Code session in that folder. A new session alone can keep
   the previous run's MLQT server, with that run's libraries still loaded: a run on 2026-10-19 did, and
   cannot be compared. Before pasting the prompt, check that this run's log file does not exist yet.
5. **Paste the scenario's prompt as it is written.** Answer a question only with the answer the scenario
   gives. Otherwise do not steer: a nudge makes the run incomparable.
6. When the agent says it is done, or after 90 minutes, stop. Note whether each item under *Done
   when* holds.
7. Read the session. `analyse` warns when the server log has calls from before the session started -
   a server that was not new, or a log shared with another run:

   ```bash
   python build/mcp-eval.py analyse --project <run folder> --latest 1 --dedicated --server-log <log> --label <scenario>-<label>
   ```

8. Add a row to the scenario's *Results* table.

## Run notes

What identifies a run's environment, beyond what is in this folder, is kept in **run notes on the
machine that runs the scenario, never in this repository**: the simulator release, the versions of
any commercial libraries, and where a starting snapshot is kept. Some of it describes licensed or
pre-release software. Each row of a *Results* table should be traceable to an entry in those notes,
by its date.

## What to compare

From the `analyse` report:
- MLQT calls, MLQT errors and ToolSearch calls;
- bypass calls, split into writes (Edit, Write, `sed -i`, scripts that write files) and reads;
- `reload` calls;
- tokens in and out, and minutes taken.

From the scenario: how many *Done when* items hold. The plan's step 1 asks for at least half fewer
bypass writes and half fewer `reload` calls than the step 0 run, with no fewer *Done when* items
holding.
