# Scenario: an overtake mode for a Formula 1 car's energy recovery, with Dymola

**Status: draft for review.**

## Where it comes from

A session on 2026-10-17 built a 2026 Formula 1 car in Dymola, as a library `Formula1` beside the three
commercial vehicle libraries it builds on. It made 416 tool calls in 86 minutes:
107 to MLQT, 61 of them `create_class`, and 74 that bypassed MLQT through the shell, mostly grep, sed
and Python edits. That `Formula1` library is about 60 classes:
- an ERS controller and energy manager, the MGU-K and its power limit, and the energy store;
- engine, gearbox, brakes, active aero, tyres, a driver with a scripted steering wheel;
- the car, a demo circuit, a flying-lap experiment and component tests.

**This scenario starts from that library rather than from nothing.** An energy recovery system written
from nothing would lack the car, the driver and the lap that give it meaning. A change to the one that
exists keeps that context. It also aims at the largest gap the plan found, **editing existing classes**:
most of the session's bypassing was patching classes it had already written. One of those patches added
an `overtakeMode` parameter to `ERSController` with a Python find-and-replace.

## Needs

- **The machine where Dymola is licensed.** It is not licensed on the machine the harness runs on, so
  this scenario runs there. Run `analyse` there too, or copy the session's transcript (under
  `~/.claude/projects/`) and the MLQT log back.
- **The Dymola release and the versions of the commercial libraries the original session used.** Every run of this
  scenario must use the same ones, or its results cannot be compared. They are recorded in the run
  notes on the test machine, not in this repository.
- **A snapshot of the `Formula1` library** as the 2026-10-17 session left it. It **must never be
  committed to this repository**, because it contains classes copied from a commercial Modelica
  library and modified. It stays on the test machine, and so do the commercial libraries. Each run copies
  the snapshot into the run folder, beside the three commercial libraries, as the original
  session had them. Where the snapshot is kept is in the run notes too.
- **A startup script** that opens the four libraries, with the Dymola MCP server configured to run it.
  The original session had none. It is added because loading what a startup script opens is a step
  the plan adds for Dymola, and this is the scenario that can test it. The script, anonymised, can also
  be one of the script-reader fixtures.
- The MLQT MCP server, and the Dymola MCP server. No other server that touches Modelica.

## Prompt

> Dymola is started with the startup script at `<startup script>`, which opens the libraries this
> project uses, including the library Formula1 in this folder. Formula1 models a 2026 Formula 1 car.
> Add a driver-selectable overtake mode to its energy recovery system. When the driver selects it, the
> MGU-K may deploy an extra 0.5 MJ per lap. Every other limit stays as it is, including the power limit
> and how the power tapers with speed. The extra energy lets the car keep accelerating harder for
> longer. Expose the mode as a parameter of the driver profile, wire it through to the ERS controller,
> and add a test of the controller that shows the difference. Then simulate the flying-lap experiment
> with and without overtake mode, and tell me the lap time, the energy deployed and the final state of
> charge for each.

**If the agent asks:**
- Should any other limit change? "No. Only the energy that may be deployed per lap, by 0.5 MJ."
- May it change other classes? "Yes, wherever the mode has to reach."

## Done when

1. The driver profile has an overtake-mode parameter, and it reaches `ERSController` through the
   classes in between. No class is duplicated to do it.
2. A new test of the controller shows its deployment allowance with the mode on and off, differing
   by 0.5 MJ per lap. The power limit and its taper are the same in both.
3. MLQT has the four libraries the startup script opens loaded, from the run folder.
4. Dymola checks the flying-lap experiment without errors, and simulates it both ways.
5. The answer gives lap time, energy deployed and final state of charge for both runs. With overtake
   mode, the energy deployed is higher by no more than 0.5 MJ, and the lap is no slower.

## Results

| Date | MLQT commit | Toolset | Done when (of 5) | Minutes | MLQT calls | MLQT errors | Bypass writes | Bypass reads | `reload` | Tokens in | Tokens out |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| 2026-10-18 | fa80416 | current | 4 of 5 | 25 | 2 | 0 | 10 | 7 | 0 | 10,714,006 | 45,340 |
| 2026-10-19 | e6e94ba | current + step 1 | 5 of 5 | 17 | 13 | 0 | 0 | 7 | 0 | 8,068,590 | 42,972 |
| 2026-10-19 | 56d1eb4 | step 1 + search-first instructions | 4 of 5, item 3 unverified | 22 | 2 | 0 | 19 | 5 | 0 | 11,695,180 | 43,170 |
| 2026-10-19 | 56d1eb4 | step 1 + search-first instructions, rerun | 3 of 5 | 18 | 0 | 0 | 13 | 8 | 0 | 8,319,313 | 31,030 |
| 2026-10-19 | bb7d8c2 | step 1 + work-naming instructions | 5 of 5 | 19 | 31 | 0 | 0 | 0 | 0 | 10,008,860 | 42,827 |
| 2026-10-19 | e10d10a | + B504 fixes | 5 of 5 | 14 | 27 | 0 | 1 | 1 | 1 | 7,755,059 | 32,274 |

**2026-10-18, current toolset: the baseline for steps 1 and 3.** Run interactively on the test machine.
The user answered one question during the run, asking for a hard SOC floor on overtake deployment,
so this run was steered once. Its MLQT log was not kept, so there are no server times.

- **Result.** Items 1, 2, 4 and 5 held.
  - With overtake mode the lap took 96.58 s against 96.79 s.
  - It deployed 5.48 MJ against 5.17 MJ, 0.31 MJ more.
  - The state of charge at the line was 11.8 % against 19.3 %.
  - The agent found that the library already had an `overtakeMode` that switched the speed taper. It
    renamed that `overtakeTaper` and gave the name to the new mode.
- **Item 3 failed.** MLQT loaded only `Formula1`. Dymola ran the startup script, but the agent never
  loaded the three libraries it opens into MLQT. It read them through the shell instead: `grep` and
  `sed` over two of the commercial libraries.
- **Almost everything went round MLQT.** Of 105 tool calls, 2 were MLQT: one `load_library` and one
  `create_class`, the new test.
  - **Writes:** 10, all through the shell. That is 8 Python find-and-replace scripts on `.mo` files,
    one `sed -i` and one `printf` into a `package.order`.
  - **Reads:** 7 shell reads of `.mo` files: `cat`, `grep`, `sed -n` and `find`.
  - **`reload`:** none in MLQT, because MLQT was never kept in step. The simulator reloaded instead:
    7 `open_model` and 3 `erase_classes`. `get_job_status` was polled 29 times while simulations ran.
- **The plan's step 1 targets these gaps:** a `replace` edit in place of the Python scripts, code
  search in place of `grep`, and loading what a startup script opens.

**2026-10-19, after step 1.** Run interactively on the test machine, with no steering. Tool Search
was on, and the build predates the instructions that point searches at `mlqt_find`. Its MLQT log
was not kept, so there are no server times.

- **Result.** All five items held.
  - With overtake mode the lap took 96.52 s against 96.80 s.
  - It deployed 5.57 MJ against 5.17 MJ, 0.40 MJ more. The new test shows the allowance at 0.500 MJ;
    the rest went on recovering 0.03 MJ less and on deployment after the line.
  - The state of charge at the line was 10.2 % against 19.3 %.
  - The agent removed the library's existing `overtakeMode`, which switched the speed taper, rather
    than renaming it as the baseline run did.
- **Item 3 held, but not through the startup script.** The agent read the script, saw that it
  pointed at an earlier copy of the run folder, and passed the four libraries to `mlqt_load` by path
  from this one. Given the script as `script`, MLQT would have loaded the other copy. That is the
  script's fault, not the reader's: Dymola would have loaded it too, as the agent said.
- **Every change to a `.mo` file went through MLQT.** Of 74 tool calls, 13 were MLQT: 2 `load`,
  10 `edit_class` and 1 `create_class`, the new test.
  - **Writes:** none to a `.mo` file, against 10. Two went to `runTests.mos`, the library's test
    script, which MLQT does not edit: adding the new test, and giving each test its own stop time.
    The rest were the agent's scratch files: a corrected copy of the startup script, a script to run
    both laps, and a Python script that summarised the results.
  - **Reads:** 7 shell reads of `.mo` files, unchanged: `cat` of the classes the change runs through,
    `grep` and `sed -n` over a commercial library, and `diff -rq` between the two copies of the run
    folder. `mlqt_find` and `mlqt_get_class_source` were not called; the next run, with the revised
    instructions, measures whether they replace these.
  - **`reload`:** none. `get_job_status` was polled 17 times while simulations ran, against 29.
- **Step 1's criterion is met**: bypass writes to the library 10 → 0, with `reload` still 0. The run
  took 17 minutes against 25, and 8.1 M input tokens against 10.7 M.

**2026-10-19, with the search-first instructions (0c23e76).** Run interactively on the test machine,
Tool Search on, with the MLQT log kept. The agent asked one question, how the extra energy should be
allowed when the per-lap limit is switched off, so the run was steered once.

- **Result.** Items 1, 2, 4 and 5 held: the lap took 96.66 s against 96.79 s, deployed 5.60 MJ against
  5.17 MJ (0.43 MJ more, 0.50 MJ of it from the allowance), and ended at 9.5 % against 19.3 %. Item 3
  is unverified: the agent passed the startup script to `mlqt_load`, and the analysis does not keep
  what the load answered.
- **The library was changed almost entirely around MLQT.** Of 95 tool calls, 2 were MLQT, both after
  the edits had begun: one `load` (9.1 s) and one `get_class_source` of a commercial library's class
  (8 ms). The agent's first tool search loaded only the simulator's tools.
  - **Writes:** 19 to the library: 3 `Edit`, 8 Python find-and-replace scripts and 4 `sed -i` on
    `.mo` files, 2 `Write` of new classes, and 2 writes to a `package.order`.
  - **Reads:** 5 shell reads of `.mo` files.
- **What changed in the instructions.** The opening sentence had said to use MLQT whenever you
  "read, create or edit Modelica code". The revision replaced that with "for any Modelica work",
  led with searching, and named only `mlqt_load` and `mlqt_find` for an agent to load. In the harness
  that moved searching to MLQT; here, with no search to do, nothing named the edit tools. One run
  each is not enough to be sure that is the cause.

**2026-10-19, the same build again.** Run to see whether the run before was variance. Tool Search
on, no steering. The transcript shows MLQT connected and its instructions delivered as written.

- **Result.** Items 1, 2 and 4 held. **Item 3 failed**: MLQT was never called, so it loaded nothing.
  **Item 5 failed**: the agent raised the per-lap deployment limit by 0.5 MJ, and since that limit is
  switched off in this model (1e9 J), both laps were identical to the digit, 96.796 s and 5.168 MJ.
  It reported that overtake mode makes no difference on this lap. It did not see that the energy has
  to come from below the state-of-charge floor, which the battery reaches 45 s in. The run before
  asked the user that question; this one did not ask.
- **No MLQT calls at all.** The agent's three tool searches loaded only the simulator's tools.
  - **Writes:** 13 to the library: 6 `Edit`, 3 Python find-and-replace scripts and 2 `sed -i` on
    `.mo` files, a `Write` of the new test, and a heredoc writing the new experiment.
  - **Reads:** 8 shell reads of `.mo` files, three of them `grep` over two commercial libraries.
- **Two runs on this wording, 2 and 0 MLQT calls; one on the step 1 wording, 13.** That is no longer
  one run against one. The step 1 opening sentence named the work - "read, create or edit Modelica
  code" - and the agent used MLQT for every edit; the search-first sentence names questions and
  searching, and in both runs the agent edited with its own tools without searching for MLQT's.

**2026-10-19, with the work-naming instructions (bb7d8c2).** Tool Search on, no steering, MLQT log
kept: 31 calls, 25 s of server time, 23 s of it the load.

- **Result.** All five items held, the first run to do so without being steered.
  - With overtake mode the lap took 96.57 s against 96.79 s, deployed 5.48 MJ against 5.17 MJ
    (0.31 MJ more: of the 0.50 MJ allowance, 0.13 MJ went mid-lap and was refilled under braking),
    and ended at 11.7 % against 19.3 %.
  - The agent saw that the energy had to come from below the state-of-charge floor, without asking.
    It renamed the existing taper switch `overtakeTaper`, as the baseline run did.
- **Item 3 held through the startup script.** `mlqt_load` with `script` loaded the four libraries and
  `Formula1` from the run folder, then the MSL and the other libraries they use from Dymola's
  library folder.
- **The work was done through MLQT.** The agent's first tool search loaded MLQT's tools, before
  any read. Of 91 tool calls, 31 were MLQT: 1 `load`, 1 `get_package_tree`, 9 `get_class_source`,
  3 `find` (`match: code`), 15 `edit_class` and 2 `create_class`.
  - **Writes and reads:** none outside MLQT. The one shell call listed the folder and printed the
    startup script.
  - **`reload`:** none. `get_job_status` was polled 21 times.
- **The dependency search used an older Dymola than the one running (B504).** The Dymola server
  started a beta release; MLQT's detection does not look for a beta's folder, so it searched the
  newest release it does look for. Four libraries the commercial libraries name were then not found
  at their versions; the nearest copies were a patch release older. It did not affect this run,
  which only read and changed `Formula1`.
- **Against the baseline:** bypass writes 10 → 0, bypass reads 7 → 0, MLQT calls 2 → 31. Against
  the two search-first runs, the same task went from 2 and 0 MLQT calls to 31. That is the
  difference between an opening sentence naming questions and one naming read, search, create and
  edit, with a tool to load for each.

**2026-10-19, with the B504 fixes (e10d10a).** Tool Search on, no steering, MLQT log kept: 27 calls,
10.7 s of server time, 8.9 s of it the load.

- **Result.** All five items held. With overtake mode the lap took 96.57 s against 96.89 s,
  deployed 5.48 MJ against 5.17 MJ (0.31 MJ more), and ended at 11.8 % against 19.3 %. The agent
  again took the energy from below the state-of-charge floor without asking. There was no separate
  `check_model` call; both laps translated and simulated.
- **The dependency search used the Dymola that was running.** Detection found the beta's folder
  and `mlqt_load` searched its library folder, saying so (`toolInstallChosen`: the newest found).
  The two libraries missing at their versions in the last run were found. Of the four, one is an
  encrypted library with no usable documentation, which the answer said, and one is installed
  nowhere on the machine.
- **MLQT did almost all of it.** The first tool search loaded MLQT's tools. Of 79 tool calls, 27
  were MLQT: 1 `load`, 10 `get_class_source`, 5 `find`, 8 `edit_class`, 2 `create_class`, 1 `reload`.
  - **Writes:** one to a `.mo` file outside MLQT: `sed -i` renaming every `normal.controlBus.` in
    the new test, followed by `mlqt_reload`. `mlqt_edit_class`'s `replace` refuses text that occurs
    more than once, which is the likely reason. One more `sed -i` added the new test to
    `runTests.mos`, which MLQT does not edit.
  - **Reads:** one, the head of a `.mo` file, beside two `package.order` reads.
  - The other shell calls read the result files with Python in the agent's scratch folder.
